using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.CountToAny;

internal sealed class CountToAnyCandidate(InvocationExpressionSyntax count, bool negate)
{
    public InvocationExpressionSyntax Count { get; } = count;

    /// <summary>True when the comparison means "is empty" (<c>!Any()</c>).</summary>
    public bool Negate { get; } = negate;
}

/// <summary>Shared by the NLR0011 analyzer and its code fix: <c>x.Count() &gt; 0</c> and its mirrored or negated forms.</summary>
internal static class CountToAnyAnalysis
{
    public static CountToAnyCandidate? Analyze(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken)
    {
        var kind = binary.Kind();
        if (kind is not (SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression or SyntaxKind.LessThanExpression
            or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression))
        {
            return null;
        }

        var literalOnLeft = binary.Left is LiteralExpressionSyntax;
        if ((literalOnLeft ? binary.Right : binary.Left) is not InvocationExpressionSyntax count
            || (literalOnLeft ? binary.Left : binary.Right) is not LiteralExpressionSyntax { Token.Value: int value })
        {
            return null;
        }

        // Read from the Count side: '0 < x.Count()' is 'x.Count() > 0'.
        if (literalOnLeft)
        {
            kind = kind switch
            {
                SyntaxKind.GreaterThanExpression => SyntaxKind.LessThanExpression,
                SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.LessThanOrEqualExpression,
                SyntaxKind.LessThanExpression => SyntaxKind.GreaterThanExpression,
                SyntaxKind.LessThanOrEqualExpression => SyntaxKind.GreaterThanOrEqualExpression,
                _ => kind,
            };
        }

        bool? isEmpty = (kind, value) switch
        {
            (SyntaxKind.GreaterThanExpression, 0) or (SyntaxKind.NotEqualsExpression, 0) or (SyntaxKind.GreaterThanOrEqualExpression, 1) => false,
            (SyntaxKind.EqualsExpression, 0) or (SyntaxKind.LessThanExpression, 1) => true,
            _ => null,
        };
        if (isEmpty is null || !IsLinqCount(count, model, cancellationToken) || LosesTrivia(binary, count, literalOnLeft))
        {
            return null;
        }

        return new CountToAnyCandidate(count, isEmpty.Value);
    }

    private static bool IsLinqCount(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" } access
            || invocation.ArgumentList.Arguments.Count > 1
            || model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { ReducedFrom: { } definition }
            || definition.ContainingType is not { Name: "Enumerable" or "Queryable", ContainingNamespace: { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } })
        {
            return false;
        }

        // A member called 'Any' on the receiver would capture the new call.
        return model.GetTypeInfo(access.Expression, cancellationToken).Type is not { } receiver
            || model.LookupSymbols(invocation.SpanStart, receiver, "Any", includeReducedExtensionMethods: false).IsEmpty;
    }

    // The literal and the operator disappear; a comment or directive next to them would be lost.
    private static bool LosesTrivia(BinaryExpressionSyntax binary, InvocationExpressionSyntax count, bool literalOnLeft)
    {
        var literal = literalOnLeft ? binary.Left.GetLastToken() : binary.Right.GetFirstToken();
        return IsLost(binary.OperatorToken.LeadingTrivia) || IsLost(binary.OperatorToken.TrailingTrivia)
            || (literalOnLeft
                ? IsLost(literal.TrailingTrivia) || IsLost(count.GetLeadingTrivia())
                : IsLost(literal.LeadingTrivia) || IsLost(count.GetTrailingTrivia()));
    }

    private static bool IsLost(SyntaxTriviaList trivia) =>
        trivia.Any(t => t.IsDirective || t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
}
