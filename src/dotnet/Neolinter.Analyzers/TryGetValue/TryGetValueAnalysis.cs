using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.IsNull;

namespace Neolinter.Analyzers.TryGetValue;

internal sealed class TryGetValueCandidate(InvocationExpressionSyntax check, ImmutableArray<ElementAccessExpressionSyntax> reads)
{
    /// <summary>The <c>d.ContainsKey(k)</c> call.</summary>
    public InvocationExpressionSyntax Check { get; } = check;

    /// <summary>The <c>d[k]</c> reads that take the value of the variable filled by <c>TryGetValue</c>.</summary>
    public ImmutableArray<ElementAccessExpressionSyntax> Reads { get; } = reads;
}

/// <summary>Shared by the NLR0015 analyzer and its code fix: <c>ContainsKey(k)</c> followed by <c>d[k]</c>.</summary>
internal static class TryGetValueAnalysis
{
    public static TryGetValueCandidate? Analyze(InvocationExpressionSyntax check, SemanticModel model, CancellationToken cancellationToken)
    {
        if (check.SyntaxTree.Options is not CSharpParseOptions options
            || options.LanguageVersion.MapSpecifiedToEffectiveVersion() < LanguageVersion.CSharp7
            || check.Expression is not MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.ValueText: "ContainsKey" } } access
            || !access.IsKind(SyntaxKind.SimpleMemberAccessExpression)
            || check.ArgumentList.Arguments.Count != 1
            || check.ArgumentList.Arguments[0] is not { NameColon: null } argument
            || argument.RefKindKeyword.RawKind != 0)
        {
            return null;
        }

        var receiver = access.Expression;
        var key = argument.Expression;
        if (!IsSimple(receiver) || !(IsSimple(key) || model.GetConstantValue(key, cancellationToken).HasValue)
            || Region(check) is not { } region
            || region.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i != check && IsContainsKey(i))
            || check.Ancestors().Any(a => a is QueryClauseSyntax or SelectOrGroupClauseSyntax or OrderingSyntax)
            || IsNullAnalysis.InExpressionTree(check, model, cancellationToken)
            || FindTryGetValue(check, receiver, model, cancellationToken) is not { } tryGet)
        {
            return null;
        }

        var reads = region.DescendantNodes().OfType<ElementAccessExpressionSyntax>()
            .Where(e => e.SpanStart >= check.Span.End
                && e.ArgumentList.Arguments.Count == 1
                && SyntaxFactory.AreEquivalent(e.Expression, receiver)
                && SyntaxFactory.AreEquivalent(e.ArgumentList.Arguments[0].Expression, key))
            .ToImmutableArray();
        if (reads.IsEmpty || !reads.All(r => IsPlainRead(r, region, tryGet, model, cancellationToken)))
        {
            return null;
        }

        return Disturbed(region, check, reads[reads.Length - 1], receiver, key) ? null : new TryGetValueCandidate(check, reads);
    }

    /// <summary>A name for the new variable that nothing in the enclosing member, or in scope there, already uses (an <c>out var</c> in an <c>if</c> condition stays in scope after it).</summary>
    public static string FreeName(InvocationExpressionSyntax check, SemanticModel model, CancellationToken cancellationToken)
    {
        var taken = new HashSet<string>(model.LookupSymbols(check.SpanStart).Select(s => s.Name));
        var member = check.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        if (member is not null)
        {
            taken.UnionWith(member.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText));
        }

        // Fix all names every fix from the original code, and the variable stays in the enclosing block: a fix that comes
        // after another one in the same member skips as many names as there are fixes before it.
        var before = member?.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Count(i => i.SpanStart < check.SpanStart && IsContainsKey(i) && Analyze(i, model, cancellationToken) is not null) ?? 0;
        for (var i = before; ; i++)
        {
            var name = i == 0 ? "value" : $"value{i}";
            if (!taken.Contains(name))
            {
                return name;
            }
        }
    }

    // Where the reads are looked for: the 'then' of 'if (d.ContainsKey(k))', the 'else' of 'if (!d.ContainsKey(k))', or the
    // rest of 'd.ContainsKey(k) && ...' when that chain is a whole condition or result (the new variable stays in its scope).
    private static SyntaxNode? Region(InvocationExpressionSyntax check)
    {
        SyntaxNode top = check;
        var negated = check.Parent is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.LogicalNotExpression);
        if (negated)
        {
            top = check.Parent!;
        }

        if (top.Parent is IfStatementSyntax ifStatement && ifStatement.Condition == top)
        {
            return negated ? ifStatement.Else?.Statement : ifStatement.Else is null ? ifStatement.Statement : null;
        }

        if (negated)
        {
            return null;
        }

        while (top.Parent is BinaryExpressionSyntax parent && parent.IsKind(SyntaxKind.LogicalAndExpression) && parent.Left == top)
        {
            top = parent;
        }

        var scoped = top.Parent switch
        {
            IfStatementSyntax s => s.Condition == top,
            ReturnStatementSyntax or ArrowExpressionClauseSyntax => true,
            LambdaExpressionSyntax l => l.Body == top,
            _ => false,
        };
        return top != check && scoped ? top : null;
    }

    private static bool IsContainsKey(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ContainsKey" };

    // An instance 'bool TryGetValue(TKey, out TValue)' that the receiver's type offers (an extension method does not count).
    private static IMethodSymbol? FindTryGetValue(InvocationExpressionSyntax check, ExpressionSyntax receiver, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(check, cancellationToken).Symbol is not IMethodSymbol { IsStatic: false, Parameters.Length: 1 } contains
            || model.GetTypeInfo(receiver, cancellationToken).Type is not { } type)
        {
            return null;
        }

        return model.LookupSymbols(check.SpanStart, type, "TryGetValue").OfType<IMethodSymbol>().FirstOrDefault(m =>
            !m.IsStatic && !m.IsGenericMethod && m.ReducedFrom is null && m.Parameters.Length == 2
            && m.Parameters[1].RefKind == RefKind.Out
            && m.ReturnType.SpecialType == SpecialType.System_Boolean
            && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, contains.Parameters[0].Type));
    }

    // The same indexer that TryGetValue mirrors, read and not written or passed by reference, and not captured by a lambda.
    private static bool IsPlainRead(ElementAccessExpressionSyntax read, SyntaxNode region, IMethodSymbol tryGet, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(read, cancellationToken).Symbol is not IPropertySymbol { IsIndexer: true, Parameters.Length: 1 } indexer
            || !SymbolEqualityComparer.Default.Equals(indexer.Type, tryGet.Parameters[1].Type)
            || !SymbolEqualityComparer.Default.Equals(indexer.Parameters[0].Type, tryGet.Parameters[0].Type)
            || read.DescendantTrivia().Any(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia)))
        {
            return false;
        }

        var written = read.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == read,
            PrefixUnaryExpressionSyntax prefix => prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression or SyntaxKind.AddressOfExpression,
            PostfixUnaryExpressionSyntax => true,
            ArgumentSyntax argument => argument.RefKindKeyword.RawKind != 0,
            _ => false,
        };
        return !written && !read.Ancestors().TakeWhile(a => a != region).Any(a => a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
    }

    // Between the check and the last read, the dictionary or the key may change (or the code may suspend): then d[k] is not the
    // value that was looked up. Calls elsewhere that change the dictionary indirectly are not detected.
    private static bool Disturbed(SyntaxNode region, InvocationExpressionSyntax check, ElementAccessExpressionSyntax lastRead, ExpressionSyntax receiver, ExpressionSyntax key)
    {
        var names = new HashSet<string>(receiver.DescendantNodesAndSelf().Concat(key.DescendantNodesAndSelf()).OfType<IdentifierNameSyntax>().Select(i => i.Identifier.ValueText));
        bool Mentions(SyntaxNode node) => node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(i => names.Contains(i.Identifier.ValueText));

        return region.DescendantNodes().Where(n => n.SpanStart >= check.Span.End && n.SpanStart < lastRead.SpanStart).Any(n => n switch
        {
            AssignmentExpressionSyntax assignment => Mentions(assignment.Left),
            PrefixUnaryExpressionSyntax prefix when prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression => Mentions(prefix.Operand),
            PostfixUnaryExpressionSyntax postfix => Mentions(postfix.Operand),
            ArgumentSyntax argument => argument.RefKindKeyword.RawKind != 0 ? Mentions(argument.Expression) : SyntaxFactory.AreEquivalent(argument.Expression, receiver),
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } =>
                SyntaxFactory.AreEquivalent(member.Expression, receiver) && member.Name.Identifier.ValueText is not ("ContainsKey" or "TryGetValue" or "ContainsValue"),
            AwaitExpressionSyntax or YieldStatementSyntax => true,
            _ => false,
        });
    }

    // Written twice and evaluated once afterwards: only a name, or a chain of member accesses.
    private static bool IsSimple(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
            access.Name is IdentifierNameSyntax && (access.Expression is ThisExpressionSyntax or BaseExpressionSyntax || IsSimple(access.Expression)),
        _ => false,
    };
}
