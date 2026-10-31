using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Neolinter.CodeFixes.InvertIf;

/// <summary>Builds the logical negation of a condition, in its simplest readable form.</summary>
internal static class ConditionNegator
{
    public static ExpressionSyntax Negate(ExpressionSyntax condition, SemanticModel model, CancellationToken cancellationToken)
    {
        var supportsNotPattern = ((CSharpParseOptions)condition.SyntaxTree.Options).LanguageVersion >= LanguageVersion.CSharp9;
        return NegateCore(condition, model, supportsNotPattern, cancellationToken).WithTriviaFrom(condition);
    }

    // Nodes passed in stay attached to the original tree (needed by the semantic model).
    // New tokens are created without elastic trivia so the host formatter leaves the user's layout alone.
    private static ExpressionSyntax NegateCore(ExpressionSyntax expression, SemanticModel model, bool supportsNotPattern, CancellationToken cancellationToken)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return NegateCore(parenthesized.Expression, model, supportsNotPattern, cancellationToken);

            case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.LogicalNotExpression):
                return StripParentheses(prefix.Operand);

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression):
                return LiteralExpression(SyntaxKind.FalseLiteralExpression, PlainToken(SyntaxKind.FalseKeyword));

            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression):
                return LiteralExpression(SyntaxKind.TrueLiteralExpression, PlainToken(SyntaxKind.TrueKeyword));

            case BinaryExpressionSyntax binary when InvertedOperator(binary, model, cancellationToken) is { } inverted:
                return BinaryExpression(inverted.Kind, binary.Left, Token(binary.OperatorToken.LeadingTrivia, inverted.Token, binary.OperatorToken.TrailingTrivia), binary.Right);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.IsExpression) && supportsNotPattern && binary.Right is TypeSyntax type:
                return IsPatternExpression(binary.Left, binary.OperatorToken, NotPattern(TypePattern(type)));

            case IsPatternExpressionSyntax isPattern when isPattern.Pattern is UnaryPatternSyntax notPattern && notPattern.IsKind(SyntaxKind.NotPattern):
                return isPattern.WithPattern(notPattern.Pattern.WithLeadingTrivia(notPattern.GetLeadingTrivia()));

            case IsPatternExpressionSyntax isPattern when supportsNotPattern:
                return isPattern.WithPattern(NotPattern(isPattern.Pattern));

            default:
                var operand = IsPrimary(expression)
                    ? expression
                    : ParenthesizedExpression(PlainToken(SyntaxKind.OpenParenToken), expression, PlainToken(SyntaxKind.CloseParenToken));
                return PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, PlainToken(SyntaxKind.ExclamationToken), operand);
        }
    }

    private static SyntaxToken PlainToken(SyntaxKind kind) => Token(TriviaList(), kind, TriviaList());

    private static UnaryPatternSyntax NotPattern(PatternSyntax pattern) =>
        UnaryPattern(Token(TriviaList(), SyntaxKind.NotKeyword, TriviaList(Space)), pattern.WithoutLeadingTrivia())
            .WithLeadingTrivia(pattern.GetLeadingTrivia());

    private static (SyntaxKind Kind, SyntaxKind Token)? InvertedOperator(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken)
    {
        switch (binary.Kind())
        {
            // Equality operators always come in consistent pairs, including NaN and lifted cases.
            case SyntaxKind.EqualsExpression:
                return (SyntaxKind.NotEqualsExpression, SyntaxKind.ExclamationEqualsToken);
            case SyntaxKind.NotEqualsExpression:
                return (SyntaxKind.EqualsExpression, SyntaxKind.EqualsEqualsToken);
        }

        // Ordering operators are only complementary without NaN, null or user-defined operators.
        if (!HasTotalOrder(binary, model, cancellationToken))
        {
            return null;
        }

        return binary.Kind() switch
        {
            SyntaxKind.LessThanExpression => (SyntaxKind.GreaterThanOrEqualExpression, SyntaxKind.GreaterThanEqualsToken),
            SyntaxKind.LessThanOrEqualExpression => (SyntaxKind.GreaterThanExpression, SyntaxKind.GreaterThanToken),
            SyntaxKind.GreaterThanExpression => (SyntaxKind.LessThanOrEqualExpression, SyntaxKind.LessThanEqualsToken),
            SyntaxKind.GreaterThanOrEqualExpression => (SyntaxKind.LessThanExpression, SyntaxKind.LessThanToken),
            _ => null,
        };
    }

    // Lifted operators report non-nullable parameters, so the operand types are checked too.
    private static bool HasTotalOrder(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetSymbolInfo(binary, cancellationToken).Symbol is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator } method
        && new[] { binary.Left, binary.Right }.All(operand => IsOrdered(model.GetTypeInfo(operand, cancellationToken).ConvertedType))
        && method.Parameters.All(p => IsOrdered(p.Type));

    private static bool IsOrdered(ITypeSymbol? type) =>
        type is not null && (type.TypeKind == TypeKind.Enum || type.SpecialType is
            SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Char or SpecialType.System_Decimal or SpecialType.System_IntPtr or SpecialType.System_UIntPtr);

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression) =>
        expression is ParenthesizedExpressionSyntax parenthesized ? StripParentheses(parenthesized.Expression) : expression;

    private static bool IsPrimary(ExpressionSyntax expression) =>
        expression is IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
            or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax or LiteralExpressionSyntax
            or ThisExpressionSyntax or BaseExpressionSyntax or ConditionalAccessExpressionSyntax;
}
