using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.CodeFixes;

/// <summary>Builds <c>!operand</c> for code fixes. Tokens carry explicit (empty) trivia, so the host keeps the user's layout.</summary>
internal static class LogicalNot
{
    public static ExpressionSyntax Of(ExpressionSyntax operand)
    {
        // '!y' negated is 'y': no double negation.
        if (operand is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return prefix.Operand;
        }

        var wrapped = BindsTighterThanNot(operand)
            ? operand
            : SyntaxFactory.ParenthesizedExpression(
                SyntaxFactory.Token(default, SyntaxKind.OpenParenToken, default),
                operand,
                SyntaxFactory.Token(default, SyntaxKind.CloseParenToken, default));

        return SyntaxFactory.PrefixUnaryExpression(
            SyntaxKind.LogicalNotExpression,
            SyntaxFactory.Token(default, SyntaxKind.ExclamationToken, default),
            wrapped);
    }

    // Anything else (binary, 'is', conditional, assignment, lambda, switch expression...) gets parentheses.
    private static bool BindsTighterThanNot(ExpressionSyntax expression) =>
        expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
            or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or LiteralExpressionSyntax or ThisExpressionSyntax
            or BaseExpressionSyntax or ConditionalAccessExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax
            or AwaitExpressionSyntax or CastExpressionSyntax or DefaultExpressionSyntax or TypeOfExpressionSyntax;
}
