using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.IsNull;

namespace Neolinter.Analyzers.IsNullOrEmpty;

internal sealed class IsNullOrEmptyCandidate(ExpressionSyntax first, ExpressionSyntax receiver, bool negate)
{
    /// <summary>The null test, which starts the pair that is replaced.</summary>
    public ExpressionSyntax First { get; } = first;

    /// <summary>The string that is tested.</summary>
    public ExpressionSyntax Receiver { get; } = receiver;

    /// <summary>True for the <c>&amp;&amp;</c> form, written <c>!string.IsNullOrEmpty(s)</c>.</summary>
    public bool Negate { get; } = negate;
}

/// <summary>Shared by the NLR0014 analyzer and its code fix: <c>s == null || s == ""</c> and <c>s != null &amp;&amp; s != ""</c>.</summary>
internal static class IsNullOrEmptyAnalysis
{
    public static IsNullOrEmptyCandidate? Analyze(BinaryExpressionSyntax node, SemanticModel model, CancellationToken cancellationToken)
    {
        var negate = node.IsKind(SyntaxKind.LogicalAndExpression);
        if (!negate && !node.IsKind(SyntaxKind.LogicalOrExpression))
        {
            return null;
        }

        // 'a || s == null || s == ""' is '(a || s == null) || s == ""': only the last two operands are looked at.
        var first = node.Left is BinaryExpressionSyntax left && left.IsKind(node.Kind()) ? left.Right : node.Left;
        if (NullTested(first, negate) is not { } receiver
            || !IsSimple(receiver)
            || !IsEmptyTest(node.Right, receiver, negate, model, cancellationToken)
            || model.GetTypeInfo(receiver, cancellationToken).Type?.SpecialType != SpecialType.System_String
            || IsNullAnalysis.InExpressionTree(node, model, cancellationToken)
            || LosesTrivia(node, first))
        {
            return null;
        }

        return new IsNullOrEmptyCandidate(first, receiver, negate);
    }

    // 's == null', 'null == s' and 's is null' (or the negated forms) give 's'.
    private static ExpressionSyntax? NullTested(ExpressionSyntax expression, bool negate)
    {
        if (expression is BinaryExpressionSyntax binary)
        {
            if (!binary.IsKind(negate ? SyntaxKind.NotEqualsExpression : SyntaxKind.EqualsExpression))
            {
                return null;
            }

            var nullOnLeft = IsNullLiteral(binary.Left);
            return nullOnLeft == IsNullLiteral(binary.Right) ? null : nullOnLeft ? binary.Right : binary.Left;
        }

        if (expression is not IsPatternExpressionSyntax isPattern)
        {
            return null;
        }

        var pattern = isPattern.Pattern;
        if (negate)
        {
            if (pattern is not UnaryPatternSyntax unary || !unary.IsKind(SyntaxKind.NotPattern))
            {
                return null;
            }

            pattern = unary.Pattern;
        }

        return pattern is ConstantPatternSyntax constant && IsNullLiteral(constant.Expression) ? isPattern.Expression : null;
    }

    // 's == ""', 's == string.Empty' and 's.Length == 0' (or '!=', and '> 0' for the negated form) on the same string.
    private static bool IsEmptyTest(ExpressionSyntax expression, ExpressionSyntax receiver, bool negate, SemanticModel model, CancellationToken cancellationToken)
    {
        if (expression is not BinaryExpressionSyntax binary)
        {
            return false;
        }

        if (binary.IsKind(negate ? SyntaxKind.NotEqualsExpression : SyntaxKind.EqualsExpression))
        {
            return (IsSame(binary.Left, receiver) && IsEmptyString(binary.Right, model, cancellationToken))
                || (IsSame(binary.Right, receiver) && IsEmptyString(binary.Left, model, cancellationToken))
                || (IsLengthOf(binary.Left, receiver) && IsZero(binary.Right))
                || (IsLengthOf(binary.Right, receiver) && IsZero(binary.Left));
        }

        return negate && binary.IsKind(SyntaxKind.GreaterThanExpression) && IsLengthOf(binary.Left, receiver) && IsZero(binary.Right);
    }

    private static bool IsNullLiteral(ExpressionSyntax expression) => expression.IsKind(SyntaxKind.NullLiteralExpression);

    private static bool IsSame(ExpressionSyntax expression, ExpressionSyntax receiver) => SyntaxFactory.AreEquivalent(expression, receiver);

    private static bool IsLengthOf(ExpressionSyntax expression, ExpressionSyntax receiver) =>
        expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Length" } access
        && access.IsKind(SyntaxKind.SimpleMemberAccessExpression)
        && IsSame(access.Expression, receiver);

    private static bool IsZero(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax { Token.Value: 0 } literal && literal.IsKind(SyntaxKind.NumericLiteralExpression);

    private static bool IsEmptyString(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken) =>
        (expression is LiteralExpressionSyntax { Token.ValueText: "" } literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        || (expression is MemberAccessExpressionSyntax
            && model.GetSymbolInfo(expression, cancellationToken).Symbol is IFieldSymbol { Name: "Empty", ContainingType.SpecialType: SpecialType.System_String });

    // The tested string is written twice and is evaluated once afterwards: only a name, or a chain of member accesses.
    private static bool IsSimple(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
            access.Name is IdentifierNameSyntax && (access.Expression is ThisExpressionSyntax or BaseExpressionSyntax || IsSimple(access.Expression)),
        _ => false,
    };

    // Everything from the null test to the end is rewritten; a comment or directive in the middle would be lost.
    private static bool LosesTrivia(BinaryExpressionSyntax node, ExpressionSyntax first)
    {
        var tokens = node.DescendantTokens().Where(t => t.SpanStart >= first.SpanStart).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            if ((i > 0 && IsNullAnalysis.IsLost(tokens[i].LeadingTrivia)) || (i < tokens.Count - 1 && IsNullAnalysis.IsLost(tokens[i].TrailingTrivia)))
            {
                return true;
            }
        }

        return false;
    }
}
