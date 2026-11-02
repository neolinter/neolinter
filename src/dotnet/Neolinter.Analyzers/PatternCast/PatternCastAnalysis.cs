using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Neolinter.Analyzers.IsNull;

namespace Neolinter.Analyzers.PatternCast;

internal enum PatternCastForm
{
    /// <summary><c>var x = e as T; if (x != null) ...</c> (the check may start a longer <c>&amp;&amp;</c> chain).</summary>
    AsThenCheck,

    /// <summary><c>var x = e as T; if (x == null) return; ...</c> with <c>x</c> used after the <c>if</c>.</summary>
    AsThenGuard,

    /// <summary><c>if (e is T) { ... (T)e ... }</c>.</summary>
    IsThenCast,
}

internal sealed class PatternCastCandidate(PatternCastForm form, BinaryExpressionSyntax node, ITypeSymbol type, IfStatementSyntax ifStatement)
{
    public PatternCastForm Form { get; } = form;

    /// <summary>The <c>e as T</c> or <c>e is T</c> expression that is reported.</summary>
    public BinaryExpressionSyntax Node { get; } = node;

    public ITypeSymbol Type { get; } = type;

    public IfStatementSyntax If { get; } = ifStatement;

    /// <summary>The <c>as</c> forms: the declaration of <c>x</c>.</summary>
    public LocalDeclarationStatementSyntax? Declaration { get; set; }

    /// <summary>The <c>as</c> forms: the name of <c>x</c>.</summary>
    public string? Name { get; set; }

    /// <summary>The <c>as</c> forms: the null check that the <c>is</c> pattern replaces.</summary>
    public ExpressionSyntax? Check { get; set; }

    /// <summary>The <c>is</c> form: the casts (or the parenthesized casts) that become the pattern variable.</summary>
    public ImmutableArray<ExpressionSyntax> Casts { get; set; }
}

/// <summary>Shared by the NLR0016 analyzer and its code fix: <c>as</c> plus null check, or <c>is</c> plus cast, as a type pattern.</summary>
internal static class PatternCastAnalysis
{
    public static PatternCastCandidate? Analyze(BinaryExpressionSyntax node, SemanticModel model, CancellationToken cancellationToken)
    {
        if (node.SyntaxTree.Options is not CSharpParseOptions options || options.LanguageVersion.MapSpecifiedToEffectiveVersion() < LanguageVersion.CSharp7
            || model.GetSymbolInfo(node.Right, cancellationToken).Symbol is not ITypeSymbol type
            || type.TypeKind is TypeKind.TypeParameter or TypeKind.Dynamic or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Error
            || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            || IsNullAnalysis.InExpressionTree(node, model, cancellationToken))
        {
            return null;
        }

        var guardAllowed = options.LanguageVersion.MapSpecifiedToEffectiveVersion() >= LanguageVersion.CSharp9;
        return node.Kind() switch
        {
            SyntaxKind.AsExpression => AnalyzeAs(node, type, guardAllowed, model, cancellationToken),
            SyntaxKind.IsExpression => AnalyzeIs(node, type, model, cancellationToken),
            _ => null,
        };
    }

    /// <summary>A name for the pattern variable of the <c>is</c> form: nothing in the enclosing member, or in scope there, uses it (an <c>if</c> condition's variable may stay in scope after it).</summary>
    public static string FreeName(PatternCastCandidate candidate, SemanticModel model, CancellationToken cancellationToken)
    {
        var node = candidate.Node;
        var taken = new HashSet<string>(model.LookupSymbols(node.SpanStart).Select(s => s.Name));
        var member = node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault();
        if (member is not null)
        {
            taken.UnionWith(member.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText));
        }

        var baseName = BaseName(candidate.Type);

        // Fix all names every fix from the original code: a fix that comes after another one with the same base name in the
        // same member skips as many names as there are fixes before it.
        var before = member?.DescendantNodes().OfType<BinaryExpressionSyntax>()
            .Count(b => b.SpanStart < node.SpanStart && b.IsKind(SyntaxKind.IsExpression)
                && Analyze(b, model, cancellationToken) is { } other && BaseName(other.Type) == baseName) ?? 0;
        for (var i = before; ; i++)
        {
            var name = i == 0 ? baseName : baseName + i;
            if (!taken.Contains(name))
            {
                return name;
            }
        }
    }

    private static string BaseName(ITypeSymbol type)
    {
        var name = type.Name;
        if (name.Length == 0)
        {
            return "value";
        }

        var camel = char.ToLowerInvariant(name[0]) + name.Substring(1);
        return SyntaxFacts.GetKeywordKind(camel) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(camel) != SyntaxKind.None ? $"{camel}Value" : camel;
    }

    // --- var x = e as T; if (x != null) ... ------------------------------------

    private static PatternCastCandidate? AnalyzeAs(BinaryExpressionSyntax node, ITypeSymbol type, bool guardAllowed, SemanticModel model, CancellationToken cancellationToken)
    {
        if (type.IsValueType
            || node.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            || declarator.Parent is not VariableDeclarationSyntax { Variables.Count: 1 } declaration
            || declaration.Parent is not LocalDeclarationStatementSyntax { Modifiers.Count: 0, Parent: BlockSyntax block } statement
            || !statement.UsingKeyword.IsKind(SyntaxKind.None) || !statement.AwaitKeyword.IsKind(SyntaxKind.None)
            || model.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol local
            || !SymbolEqualityComparer.Default.Equals(local.Type, type)
            || !IsBlank(statement.DescendantTrivia(TextSpan.FromBounds(statement.SpanStart, node.Left.SpanStart)))
            || !IsBlank(statement.DescendantTrivia(TextSpan.FromBounds(node.Left.Span.End, statement.Span.End)))
            || !IsBlank(statement.GetTrailingTrivia()))
        {
            return null;
        }

        var index = block.Statements.IndexOf(statement);
        if (index + 1 >= block.Statements.Count || block.Statements[index + 1] is not IfStatementSyntax ifStatement
            || !IsBlank(ifStatement.GetLeadingTrivia()))
        {
            return null;
        }

        bool UsesLocal(SyntaxNode? scope) => scope is not null && scope.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Any(i => i.Identifier.ValueText == local.Name && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(i, cancellationToken).Symbol, local));
        var usedAfter = block.Statements.Skip(index + 2).Any(UsesLocal);

        var leftmost = ifStatement.Condition;
        while (leftmost is BinaryExpressionSyntax chain && chain.IsKind(SyntaxKind.LogicalAndExpression))
        {
            leftmost = chain.Left;
        }

        if (IsNullTest(leftmost, local, notNull: true, model, cancellationToken))
        {
            return usedAfter || UsesLocal(ifStatement.Else) ? null : Make(PatternCastForm.AsThenCheck, leftmost);
        }

        // 'if (x == null) <exit>' then 'x' after the 'if': 'e is not T x' keeps 'x' in scope for the rest of the block.
        if (guardAllowed && ifStatement.Else is null && IsNullTest(ifStatement.Condition, local, notNull: false, model, cancellationToken)
            && usedAfter && !UsesLocal(ifStatement.Statement)
            && model.AnalyzeControlFlow(ifStatement.Statement) is { Succeeded: true, EndPointIsReachable: false })
        {
            return Make(PatternCastForm.AsThenGuard, ifStatement.Condition);
        }

        return null;

        PatternCastCandidate Make(PatternCastForm form, ExpressionSyntax check) =>
            new(form, node, type, ifStatement) { Declaration = statement, Name = local.Name, Check = check };
    }

    // 'x != null', 'null != x' and 'x is not null' (notNull), or 'x == null', 'null == x' and 'x is null'.
    private static bool IsNullTest(ExpressionSyntax test, ILocalSymbol local, bool notNull, SemanticModel model, CancellationToken cancellationToken)
    {
        bool IsLocal(ExpressionSyntax e) => e is IdentifierNameSyntax i && i.Identifier.ValueText == local.Name
            && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(i, cancellationToken).Symbol, local);
        bool IsNull(ExpressionSyntax e) => e.IsKind(SyntaxKind.NullLiteralExpression);

        switch (test)
        {
            case BinaryExpressionSyntax binary when binary.IsKind(notNull ? SyntaxKind.NotEqualsExpression : SyntaxKind.EqualsExpression):
                return (IsLocal(binary.Left) && IsNull(binary.Right) || IsLocal(binary.Right) && IsNull(binary.Left))
                    && !HasCustomOperator(binary, model, cancellationToken);
            case IsPatternExpressionSyntax isPattern when IsLocal(isPattern.Expression):
                return notNull
                    ? isPattern.Pattern is UnaryPatternSyntax unary && unary.OperatorToken.IsKind(SyntaxKind.NotKeyword)
                        && unary.Pattern is ConstantPatternSyntax notNullConstant && IsNull(notNullConstant.Expression)
                    : isPattern.Pattern is ConstantPatternSyntax constant && IsNull(constant.Expression);
            default:
                return false;
        }
    }

    // A user-defined '==' or '!=' may mean something else than 'is null'; string and delegates compare to null as expected.
    private static bool HasCustomOperator(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetSymbolInfo(binary, cancellationToken).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method
        && method.ContainingType.SpecialType is not (SpecialType.System_String or SpecialType.System_Delegate or SpecialType.System_MulticastDelegate);

    // --- if (e is T) { ... (T)e ... } -----------------------------------------

    private static PatternCastCandidate? AnalyzeIs(BinaryExpressionSyntax node, ITypeSymbol type, SemanticModel model, CancellationToken cancellationToken)
    {
        if (node.Parent is not IfStatementSyntax ifStatement || ifStatement.Condition != node)
        {
            return null;
        }

        var shared = false;
        var region = ifStatement.Statement;
        var e = node.Left;
        if (!Stable(e, model, cancellationToken, ref shared)
            || region.DescendantNodes().Any(n => n.Kind() is SyntaxKind.AsExpression or SyntaxKind.IsExpression)
            || shared && region.DescendantNodes().Any(n => n is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax))
        {
            return null;
        }

        var casts = ImmutableArray.CreateBuilder<ExpressionSyntax>();
        CastExpressionSyntax? last = null;
        foreach (var cast in region.DescendantNodes().OfType<CastExpressionSyntax>())
        {
            if (!SyntaxFactory.AreEquivalent(cast.Expression, e)
                || model.GetSymbolInfo(cast.Type, cancellationToken).Symbol is not ITypeSymbol target
                || !SymbolEqualityComparer.Default.Equals(target, type))
            {
                continue;
            }

            // The cast must be the plain reference or boxing conversion that 'is' tests, not a user-defined or numeric one.
            var conversion = model.GetConversion(cast, cancellationToken);
            if (!(conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing || conversion.IsUnboxing)
                || cast.DescendantTrivia().Any(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia))
                || cast.Ancestors().TakeWhile(a => a != region).Any(a => a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            {
                return null;
            }

            casts.Add(cast.Parent is ParenthesizedExpressionSyntax parenthesized ? parenthesized : cast);
            last = cast;
        }

        if (last is null || Disturbed(region, last, e, shared))
        {
            return null;
        }

        return new PatternCastCandidate(PatternCastForm.IsThenCast, node, type, ifStatement) { Casts = casts.ToImmutable() };
    }

    // The same value must be read again: a local, a parameter or a field (a property getter may return something else), or a
    // chain of fields. 'shared' says that something other than this code could change it (a field).
    private static bool Stable(ExpressionSyntax e, SemanticModel model, CancellationToken cancellationToken, ref bool shared)
    {
        switch (e)
        {
            case ThisExpressionSyntax or BaseExpressionSyntax:
                return true;
            case IdentifierNameSyntax:
                switch (model.GetSymbolInfo(e, cancellationToken).Symbol)
                {
                    case ILocalSymbol or IParameterSymbol:
                        return true;
                    case IFieldSymbol:
                        shared = true;
                        return true;
                    default:
                        return false;
                }

            case MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                && model.GetSymbolInfo(access.Name, cancellationToken).Symbol is IFieldSymbol:
                shared = true;
                return model.GetSymbolInfo(access.Expression, cancellationToken).Symbol is ITypeSymbol || Stable(access.Expression, model, cancellationToken, ref shared);
            default:
                return false;
        }
    }

    // Between the check and the last cast the value may change: it is assigned (or passed by reference) anywhere in the guarded
    // code, or, for a field, a call, an object creation, an assignment or an await comes before the last cast.
    private static bool Disturbed(SyntaxNode region, CastExpressionSyntax last, ExpressionSyntax e, bool shared)
    {
        var names = new HashSet<string>(e.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(i => i.Identifier.ValueText));
        bool Mentions(SyntaxNode node) => node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(i => names.Contains(i.Identifier.ValueText));

        return region.DescendantNodes().Any(n => n switch
        {
            AssignmentExpressionSyntax assignment => Mentions(assignment.Left) || shared && assignment.Span.End <= last.SpanStart,
            PrefixUnaryExpressionSyntax prefix when prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression or SyntaxKind.AddressOfExpression => Mentions(prefix.Operand),
            PostfixUnaryExpressionSyntax postfix => Mentions(postfix.Operand),
            ArgumentSyntax argument => argument.RefKindKeyword.RawKind != 0 && Mentions(argument.Expression),
            InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or AwaitExpressionSyntax or YieldStatementSyntax => shared && n.Span.End <= last.SpanStart,
            _ => false,
        });
    }

    private static bool IsBlank(IEnumerable<SyntaxTrivia> trivia) => trivia.All(t => t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia));
}
