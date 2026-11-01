using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.BooleanComparison;

/// <summary>A comparison with <c>true</c> or <c>false</c> that the value itself (or its negation) can replace.</summary>
internal sealed class BooleanComparisonCandidate(BinaryExpressionSyntax comparison, ExpressionSyntax operand, bool negate)
{
    public BinaryExpressionSyntax Comparison { get; } = comparison;

    /// <summary>The side that is not the literal.</summary>
    public ExpressionSyntax Operand { get; } = operand;

    /// <summary>True when the replacement is <c>!operand</c>.</summary>
    public bool Negate { get; } = negate;
}

/// <summary>
/// Finds <c>x == true</c>, <c>x != false</c> and the other forms (literal on either side) where <c>x</c> is a plain
/// <c>bool</c> and the operator is the built-in one. Shared by the analyzer and the code fix.
/// </summary>
internal static class BooleanComparisonAnalysis
{
    public static BooleanComparisonCandidate? Analyze(BinaryExpressionSyntax comparison, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!comparison.IsKind(SyntaxKind.EqualsExpression) && !comparison.IsKind(SyntaxKind.NotEqualsExpression))
        {
            return null;
        }

        var rightIsLiteral = IsBooleanLiteral(comparison.Right, out var literal);
        if (!rightIsLiteral && !IsBooleanLiteral(comparison.Left, out literal))
        {
            return null;
        }

        var operand = rightIsLiteral ? comparison.Left : comparison.Right;

        // 'true == false' is a constant, not a comparison to simplify.
        if (IsBooleanLiteral(operand, out _) || HasInnerComment(comparison))
        {
            return null;
        }

        // Only the built-in bool == bool: a bool? operand is lifted (its type is Nullable<bool>), and a user-defined
        // operator or an implicit conversion to bool gives a symbol or a type that is not the built-in one.
        if (model.GetTypeInfo(operand, cancellationToken).Type?.SpecialType != SpecialType.System_Boolean
            || model.GetSymbolInfo(comparison, cancellationToken).Symbol is not IMethodSymbol { MethodKind: MethodKind.BuiltinOperator })
        {
            return null;
        }

        var negate = comparison.IsKind(SyntaxKind.EqualsExpression) != literal;
        return new BooleanComparisonCandidate(comparison, operand, negate);
    }

    private static bool IsBooleanLiteral(ExpressionSyntax expression, out bool value)
    {
        value = expression.IsKind(SyntaxKind.TrueLiteralExpression);
        return value || expression.IsKind(SyntaxKind.FalseLiteralExpression);
    }

    // The rewrite drops the operator and the literal, so a comment or directive between them would be lost.
    private static bool HasInnerComment(SyntaxNode node) =>
        node.WithoutTrivia().DescendantTrivia().Any(trivia =>
            trivia.IsDirective
            || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));
}
