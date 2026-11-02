using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.CountToAny;

namespace Neolinter.Analyzers.WhereTerminal;

internal sealed class WhereTerminalCandidate(InvocationExpressionSyntax terminal, InvocationExpressionSyntax where)
{
    /// <summary>The <c>.Any()</c> / <c>.First()</c> ... call.</summary>
    public InvocationExpressionSyntax Terminal { get; } = terminal;

    /// <summary>The <c>.Where(p)</c> call that becomes the terminal call.</summary>
    public InvocationExpressionSyntax Where { get; } = where;
}

/// <summary>Shared by the NLR0012 analyzer and its code fix: <c>x.Where(p).Any()</c> and the other terminal operators.</summary>
internal static class WhereTerminalAnalysis
{
    private static readonly string[] Terminals =
        ["Any", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault", "Count", "LongCount"];

    public static WhereTerminalCandidate? Analyze(InvocationExpressionSyntax terminal, SemanticModel model, CancellationToken cancellationToken)
    {
        if (terminal.ArgumentList.Arguments.Count != 0
            || terminal.Expression is not MemberAccessExpressionSyntax { Name: var name } access
            || Array.IndexOf(Terminals, name.Identifier.ValueText) < 0
            || access.Expression is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Where" } whereAccess, ArgumentList.Arguments.Count: 1 } where
            || !IsLinq(terminal, model, cancellationToken, out _)
            || !IsLinq(where, model, cancellationToken, out var whereDefinition)
            || !IsSingleParameterPredicate(whereDefinition))
        {
            return null;
        }

        // NLR0011 owns 'Where(p).Count() > 0': its fix leaves 'Where(p).Any()', which this rule then takes.
        if (terminal.Parent is BinaryExpressionSyntax comparison && CountToAnyAnalysis.Analyze(comparison, model, cancellationToken) is not null)
        {
            return null;
        }

        // A member of the same name on the receiver would capture the new call.
        if (model.GetTypeInfo(whereAccess.Expression, cancellationToken).Type is { } receiver
            && !model.LookupSymbols(terminal.SpanStart, receiver, name.Identifier.ValueText, includeReducedExtensionMethods: false).IsEmpty)
        {
            return null;
        }

        return LosesTrivia(terminal, access, where) ? null : new WhereTerminalCandidate(terminal, where);
    }

    private static bool IsLinq(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken, out IMethodSymbol definition)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol { ReducedFrom: { } reduced }
            && reduced.ContainingType is { Name: "Enumerable" or "Queryable", ContainingNamespace: { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } })
        {
            definition = reduced;
            return true;
        }

        definition = null!;
        return false;
    }

    // Skips the overload taking the index: Where<T>(Func<T, int, bool>).
    private static bool IsSingleParameterPredicate(IMethodSymbol definition)
    {
        var predicate = definition.Parameters.Length == 2 ? definition.Parameters[1].Type as INamedTypeSymbol : null;
        if (predicate is { Name: "Expression" })
        {
            predicate = predicate.TypeArguments[0] as INamedTypeSymbol;
        }

        return predicate?.DelegateInvokeMethod?.Parameters.Length == 1;
    }

    // The terminal call's dot, name and parentheses disappear, and so does the trailing trivia of the Where call.
    private static bool LosesTrivia(InvocationExpressionSyntax terminal, MemberAccessExpressionSyntax access, InvocationExpressionSyntax where) =>
        IsLost(access.OperatorToken.LeadingTrivia) || IsLost(access.OperatorToken.TrailingTrivia)
        || IsLost(access.Name.GetLeadingTrivia()) || IsLost(access.Name.GetTrailingTrivia())
        || IsLost(terminal.ArgumentList.OpenParenToken.LeadingTrivia) || IsLost(terminal.ArgumentList.OpenParenToken.TrailingTrivia)
        || IsLost(terminal.ArgumentList.CloseParenToken.LeadingTrivia)
        || IsLost(where.ArgumentList.CloseParenToken.TrailingTrivia);

    private static bool IsLost(SyntaxTriviaList trivia) =>
        trivia.Any(t => t.IsDirective || t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
}
