using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.IsNull;

internal sealed class IsNullCandidate(ExpressionSyntax operand, bool negate)
{
    /// <summary>The side that is not the <c>null</c> literal.</summary>
    public ExpressionSyntax Operand { get; } = operand;

    /// <summary>True for <c>!=</c>, written <c>is not null</c>.</summary>
    public bool Negate { get; } = negate;
}

/// <summary>Shared by the NLR0013 analyzer and its code fix: <c>x == null</c>, <c>null == x</c> and the <c>!=</c> forms.</summary>
internal static class IsNullAnalysis
{
    public static IsNullCandidate? Analyze(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken)
    {
        var negate = binary.IsKind(SyntaxKind.NotEqualsExpression);
        if ((!negate && !binary.IsKind(SyntaxKind.EqualsExpression))
            || binary.SyntaxTree.Options is not CSharpParseOptions options
            || options.LanguageVersion.MapSpecifiedToEffectiveVersion() < (negate ? LanguageVersion.CSharp9 : LanguageVersion.CSharp7))
        {
            return null;
        }

        var nullOnLeft = IsNullLiteral(binary.Left);
        if (nullOnLeft == IsNullLiteral(binary.Right))
        {
            return null;
        }

        var operand = nullOnLeft ? binary.Right : binary.Left;
        if (!CanBeNull(model.GetTypeInfo(operand, cancellationToken).Type)
            || !IsPredefinedOrHarmless(binary, model, cancellationToken)
            || InExpressionTree(binary, model, cancellationToken)
            || LosesTrivia(binary, operand, nullOnLeft))
        {
            return null;
        }

        return new IsNullCandidate(operand, negate);
    }

    private static bool IsNullLiteral(ExpressionSyntax expression) => expression.IsKind(SyntaxKind.NullLiteralExpression);

    // 'is null' is an error on a non-nullable value type, and on pointers.
    private static bool CanBeNull(ITypeSymbol? type) =>
        type is { TypeKind: not (TypeKind.Error or TypeKind.Dynamic or TypeKind.Pointer or TypeKind.FunctionPointer) }
        && (!type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

    // A user-defined == on a reference type may not mean "is null"; 'is null' never calls it. string's own operator does, and
    // a lifted operator on a nullable value type is not called with a null side.
    private static bool IsPredefinedOrHarmless(BinaryExpressionSyntax binary, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetSymbolInfo(binary, cancellationToken).Symbol is IMethodSymbol { MethodKind: var kind } method
        && (kind != MethodKind.UserDefinedOperator
            || method.ContainingType.SpecialType == SpecialType.System_String
            || method.ContainingType.IsValueType);

    // 'is null' is not allowed in an expression tree (an Entity Framework Core query, for example).
    internal static bool InExpressionTree(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case LambdaExpressionSyntax or AnonymousMethodExpressionSyntax
                    when IsExpressionType(model.GetTypeInfo(ancestor, cancellationToken).ConvertedType):
                    return true;
                case QueryClauseSyntax or SelectOrGroupClauseSyntax or OrderingSyntax:
                    // The clause becomes a call taking a lambda: look at what it is converted to.
                    var clause = ancestor is QueryClauseSyntax queryClause
                        ? model.GetQueryClauseInfo(queryClause, cancellationToken).OperationInfo
                        : model.GetSymbolInfo(ancestor, cancellationToken);
                    if (clause.Symbol is not IMethodSymbol method
                        || method.Parameters.Any(p => IsExpressionType(p.Type)))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static bool IsExpressionType(ITypeSymbol? type) =>
        type?.OriginalDefinition is { Name: "Expression", ContainingNamespace: { Name: "Expressions", ContainingNamespace: { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } } };

    // The literal and the operator are rewritten; a comment or directive next to them would be lost.
    private static bool LosesTrivia(BinaryExpressionSyntax binary, ExpressionSyntax operand, bool nullOnLeft)
    {
        var literal = nullOnLeft ? binary.Left : binary.Right;
        return IsLost(binary.OperatorToken.LeadingTrivia) || IsLost(binary.OperatorToken.TrailingTrivia)
            || (nullOnLeft
                ? IsLost(literal.GetTrailingTrivia()) || IsLost(operand.GetLeadingTrivia())
                : IsLost(literal.GetLeadingTrivia()) || IsLost(operand.GetTrailingTrivia()));
    }

    internal static bool IsLost(SyntaxTriviaList trivia) =>
        trivia.Any(t => t.IsDirective || t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
}
