using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.NullCoalescingAssignment;

/// <summary>A statement that <c>??=</c> can replace.</summary>
internal sealed class NullCoalescingAssignmentCandidate(StatementSyntax statement, SyntaxToken anchor, ExpressionSyntax target, ExpressionSyntax value)
{
    /// <summary>The whole statement that the fix replaces (an <c>if</c> or an expression statement).</summary>
    public StatementSyntax Statement { get; } = statement;

    /// <summary>The token the diagnostic is reported on.</summary>
    public SyntaxToken Anchor { get; } = anchor;

    public ExpressionSyntax Target { get; } = target;

    public ExpressionSyntax Value { get; } = value;
}

/// <summary>
/// Finds <c>if (x == null) x = v;</c> (also <c>null == x</c>, <c>x is null</c>, with braces) and <c>x = x ?? v;</c>.
/// Shared by the analyzer and the code fix. Anything that could change behaviour is left alone: see
/// <c>rules/NLR0004.yaml</c> for the list.
/// </summary>
internal static class NullCoalescingAssignmentAnalysis
{
    public static NullCoalescingAssignmentCandidate? Analyze(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        if (node.SyntaxTree.Options is not CSharpParseOptions options
            || options.LanguageVersion.MapSpecifiedToEffectiveVersion() < LanguageVersion.CSharp8)
        {
            return null;
        }

        var candidate = node switch
        {
            IfStatementSyntax ifStatement => FromIf(ifStatement, model, cancellationToken),
            ExpressionStatementSyntax statement => FromCoalesce(statement, model, cancellationToken),
            _ => null,
        };

        // A comment or directive inside the statement would be lost by the rewrite.
        return candidate is null || HasInnerTrivia(node) ? null : candidate;
    }

    private static NullCoalescingAssignmentCandidate? FromIf(IfStatementSyntax ifStatement, SemanticModel model, CancellationToken cancellationToken)
    {
        if (ifStatement.Else is not null
            || SingleAssignment(ifStatement.Statement) is not { } assignment
            || TestedForNull(ifStatement.Condition, model, cancellationToken) is not { } tested
            || !SyntaxFactory.AreEquivalent(tested, assignment.Left)
            || !IsSafe(assignment.Left, assignment.Right, autoPropertyOnly: false, model, cancellationToken))
        {
            return null;
        }

        return new NullCoalescingAssignmentCandidate(ifStatement, ifStatement.IfKeyword, assignment.Left, assignment.Right);
    }

    private static NullCoalescingAssignmentCandidate? FromCoalesce(ExpressionStatementSyntax statement, SemanticModel model, CancellationToken cancellationToken)
    {
        // x = x ?? v calls a property setter even when x is not null, x ??= v does not: only auto-properties qualify.
        if (statement.Expression is not AssignmentExpressionSyntax assignment
            || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || assignment.Right is not BinaryExpressionSyntax coalesce
            || !coalesce.IsKind(SyntaxKind.CoalesceExpression)
            || !SyntaxFactory.AreEquivalent(assignment.Left, coalesce.Left)
            || !IsSafe(assignment.Left, coalesce.Right, autoPropertyOnly: true, model, cancellationToken))
        {
            return null;
        }

        return new NullCoalescingAssignmentCandidate(statement, assignment.OperatorToken, assignment.Left, coalesce.Right);
    }

    private static AssignmentExpressionSyntax? SingleAssignment(StatementSyntax statement)
    {
        if (statement is BlockSyntax { Statements.Count: 1 } block)
        {
            statement = block.Statements[0];
        }

        return statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                ? assignment
                : null;
    }

    /// <summary>The expression compared with null by <c>x == null</c>, <c>null == x</c> or <c>x is null</c>.</summary>
    private static ExpressionSyntax? TestedForNull(ExpressionSyntax condition, SemanticModel model, CancellationToken cancellationToken)
    {
        if (condition is IsPatternExpressionSyntax { Pattern: ConstantPatternSyntax { Expression: var constant } } pattern)
        {
            return IsNull(constant) ? pattern.Expression : null;
        }

        if (condition is not BinaryExpressionSyntax binary || !binary.IsKind(SyntaxKind.EqualsExpression))
        {
            return null;
        }

        // A user-defined == may not mean "is null"; ??= never calls it.
        if (model.GetSymbolInfo(binary, cancellationToken).Symbol is IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator })
        {
            return null;
        }

        return IsNull(binary.Right) ? binary.Left : IsNull(binary.Left) ? binary.Right : null;
    }

    private static bool IsNull(ExpressionSyntax expression) => expression.IsKind(SyntaxKind.NullLiteralExpression);

    private static bool IsSafe(ExpressionSyntax target, ExpressionSyntax value, bool autoPropertyOnly, SemanticModel model, CancellationToken cancellationToken)
    {
        // ??= does not take a throw expression, and a null/default literal has no type of its own to convert from.
        if (value is ThrowExpressionSyntax or RefExpressionSyntax
            || value.IsKind(SyntaxKind.NullLiteralExpression)
            || value.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            return false;
        }

        return IsStableTarget(target, isReceiver: false, autoPropertyOnly, model, cancellationToken)
            && IsNullable(model.GetTypeInfo(target, cancellationToken).Type);
    }

    // Reference types and Nullable<T>. Non-nullable value types, pointers, dynamic and unconstrained type parameters are out.
    private static bool IsNullable(ITypeSymbol? type) =>
        type is not null
        && type.TypeKind != TypeKind.Dynamic
        && (type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);

    /// <summary>
    /// The long forms evaluate the target more than once, <c>??=</c> once, so evaluating it must be free of side
    /// effects: locals, parameters, fields (not volatile), <c>this</c>, type names and, as the last member only, a
    /// non-indexer property.
    /// </summary>
    private static bool IsStableTarget(ExpressionSyntax expression, bool isReceiver, bool autoPropertyOnly, SemanticModel model, CancellationToken cancellationToken)
    {
        switch (expression)
        {
            case ThisExpressionSyntax:
                return isReceiver;
            case IdentifierNameSyntax:
                return IsStableSymbol(model.GetSymbolInfo(expression, cancellationToken).Symbol, isReceiver, autoPropertyOnly);
            case MemberAccessExpressionSyntax member when member.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                return IsStableSymbol(model.GetSymbolInfo(member, cancellationToken).Symbol, isReceiver, autoPropertyOnly)
                    && IsStableTarget(member.Expression, isReceiver: true, autoPropertyOnly, model, cancellationToken);
            default:
                return false;
        }
    }

    private static bool IsStableSymbol(ISymbol? symbol, bool isReceiver, bool autoPropertyOnly) => symbol switch
    {
        ILocalSymbol or IParameterSymbol => true,
        IFieldSymbol field => !field.IsVolatile,
        IPropertySymbol property => !isReceiver && !property.IsIndexer && (!autoPropertyOnly || IsPlainAutoProperty(property)),
        INamedTypeSymbol or INamespaceSymbol => isReceiver,
        _ => false,
    };

    // Declared in this source with no accessor body, and not overridable (a derived setter could do anything).
    private static bool IsPlainAutoProperty(IPropertySymbol property) =>
        !property.IsVirtual
        && !property.IsOverride
        && !property.IsAbstract
        && property.DeclaringSyntaxReferences.Length == 1
        && property.DeclaringSyntaxReferences[0].GetSyntax() is PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } accessorList }
        && accessorList.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null);

    private static bool HasInnerTrivia(SyntaxNode node) =>
        node.WithoutTrivia().DescendantTrivia().Any(trivia =>
            trivia.IsDirective
            || trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
}
