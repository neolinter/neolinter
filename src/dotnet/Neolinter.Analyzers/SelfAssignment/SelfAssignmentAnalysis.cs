using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.SelfAssignment;

/// <summary>
/// Shared by the NLR0029 analyzer and code fix. A simple assignment is reported when both sides name the same local,
/// parameter, field or auto-implemented property through the same receiver chain (<c>x = x</c>, <c>this.a = a</c> where
/// <c>a</c> is the field, <c>o.a = o.a</c>). Not reported: calls, indexers and other receivers that could differ or have
/// side effects, and properties with a body or that can be overridden.
/// </summary>
internal static class SelfAssignmentAnalysis
{
    public static bool IsReportable(AssignmentExpressionSyntax node, SemanticModel model, CancellationToken cancellationToken) =>
        node.IsKind(SyntaxKind.SimpleAssignmentExpression)
        && TryGetMember(node.Left, model, cancellationToken, out var leftSymbol, out var leftReceiver)
        && TryGetMember(node.Right, model, cancellationToken, out var rightSymbol, out var rightReceiver)
        && SymbolEqualityComparer.Default.Equals(leftSymbol, rightSymbol)
        && SameReceiver(leftReceiver, rightReceiver, model, cancellationToken);

    /// <summary>The fix removes the statement, which is only safe when it is a plain statement of a block.</summary>
    public static bool IsRemovable(AssignmentExpressionSyntax node) =>
        node.Parent is ExpressionStatementSyntax { Parent: BlockSyntax or SwitchSectionSyntax };

    private static bool TryGetMember(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken, out ISymbol symbol, out ExpressionSyntax? receiver)
    {
        symbol = null!;
        receiver = null;
        expression = Unwrap(expression);
        ExpressionSyntax name;
        switch (expression)
        {
            case IdentifierNameSyntax:
                name = expression;
                break;
            case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } access:
                name = access.Name;
                receiver = Unwrap(access.Expression);
                break;
            default:
                return false;
        }

        if (model.GetSymbolInfo(name, cancellationToken).Symbol is not { } found || !IsPlain(found))
        {
            return false;
        }

        symbol = found;
        return true;
    }

    private static bool IsPlain(ISymbol symbol) => symbol switch
    {
        ILocalSymbol or IParameterSymbol or IFieldSymbol => true,
        IPropertySymbol { IsIndexer: false, IsVirtual: false, IsOverride: false, IsAbstract: false } property => IsAutoImplemented(property),
        _ => false,
    };

    private static bool IsAutoImplemented(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.Length > 0
        && property.DeclaringSyntaxReferences.All(r => r.GetSyntax() is PropertyDeclarationSyntax { AccessorList: { } list }
            && list.Accessors.All(a => a.Body is null && a.ExpressionBody is null));

    private static bool SameReceiver(ExpressionSyntax? left, ExpressionSyntax? right, SemanticModel model, CancellationToken cancellationToken)
    {
        // No receiver and 'this' both mean the current instance.
        if (IsThis(left) || IsThis(right))
        {
            return IsThis(left) && IsThis(right);
        }

        if (!TryGetMember(left!, model, cancellationToken, out var leftSymbol, out var leftReceiver)
            || !TryGetMember(right!, model, cancellationToken, out var rightSymbol, out var rightReceiver))
        {
            // A type name used as receiver of a static member: C.x = C.x.
            return model.GetSymbolInfo(left!, cancellationToken).Symbol is INamedTypeSymbol l
                && model.GetSymbolInfo(right!, cancellationToken).Symbol is INamedTypeSymbol r
                && SymbolEqualityComparer.Default.Equals(l, r);
        }

        return SymbolEqualityComparer.Default.Equals(leftSymbol, rightSymbol)
            && SameReceiver(leftReceiver, rightReceiver, model, cancellationToken);
    }

    private static bool IsThis(ExpressionSyntax? receiver) => receiver is null or ThisExpressionSyntax;

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
