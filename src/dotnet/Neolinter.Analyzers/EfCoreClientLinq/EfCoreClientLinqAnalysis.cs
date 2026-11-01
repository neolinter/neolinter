using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.EfCoreClientLinq;

/// <summary>
/// Detection for NLR0006: a LINQ operator called directly on the loaded result of an Entity Framework Core query.
/// Entity Framework Core is recognized by type name only (<c>Microsoft.EntityFrameworkCore.DbSet&lt;T&gt;</c>), so the
/// analyzer needs no reference to the package.
/// </summary>
internal static class EfCoreClientLinqAnalysis
{
    private const string EfNamespace = "Microsoft.EntityFrameworkCore";

    private static readonly ImmutableHashSet<string> Operators = ImmutableHashSet.Create(
        "Select", "SelectMany", "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "GroupBy",
        "Count", "LongCount", "Any", "All", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last",
        "LastOrDefault", "Skip", "Take", "Distinct", "Sum", "Min", "Max", "Average");

    private static readonly ImmutableHashSet<string> SyncMaterializers = ImmutableHashSet.Create(
        "ToList", "ToArray", "ToDictionary", "AsEnumerable");

    private static readonly ImmutableHashSet<string> AsyncMaterializers = ImmutableHashSet.Create(
        "ToListAsync", "ToArrayAsync", "ToDictionaryAsync");

    /// <summary>Returns the operator name to report and the materializing method it follows, or false.</summary>
    internal static bool TryFind(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken, out SimpleNameSyntax operatorName, out string materializer)
    {
        operatorName = null!;
        materializer = "";

        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: { } name } access
            || !Operators.Contains(name.Identifier.ValueText)
            || Strip(access.Expression) is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name: { } materializerName } materializerAccess }
            || !(SyncMaterializers.Contains(materializerName.Identifier.ValueText) || AsyncMaterializers.Contains(materializerName.Identifier.ValueText)))
        {
            return false;
        }

        var materialization = (InvocationExpressionSyntax)Strip(access.Expression);
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { ContainingType: { } linq }
            || !IsType(linq, "System.Linq", "Enumerable")
            || model.GetSymbolInfo(materialization, cancellationToken).Symbol is not IMethodSymbol { ContainingType: { } owner } method
            || !IsMaterializer(method.Name, owner)
            || !IsEfQuery(materializerAccess.Expression, model, cancellationToken))
        {
            return false;
        }

        operatorName = name;
        materializer = method.Name;
        return true;
    }

    private static bool IsMaterializer(string name, INamedTypeSymbol owner) =>
        SyncMaterializers.Contains(name)
            ? IsType(owner, "System.Linq", "Enumerable")
            : AsyncMaterializers.Contains(name) && owner.ContainingNamespace.ToDisplayString().StartsWith(EfNamespace, StringComparison.Ordinal);

    /// <summary>True when the expression is a <c>DbSet</c>, or an <c>IQueryable</c> built from one with query operators.</summary>
    private static bool IsEfQuery(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        expression = Strip(expression);
        if (model.GetTypeInfo(expression, cancellationToken).Type is not { } type)
        {
            return false;
        }

        if (IsDbSet(type))
        {
            return true;
        }

        // The step must still be a query: First() on an IQueryable of lists returns a plain list.
        return IsQueryable(type)
            && expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call
            && model.GetSymbolInfo(call, cancellationToken).Symbol is IMethodSymbol { IsExtensionMethod: true, ContainingType: { } owner }
            && (IsType(owner, "System.Linq", "Queryable") || owner.ContainingNamespace.ToDisplayString().StartsWith(EfNamespace, StringComparison.Ordinal))
            && IsEfQuery(access.Expression, model, cancellationToken);
    }

    private static bool IsDbSet(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current is { Name: "DbSet", Arity: 1 } && current.ContainingNamespace.ToDisplayString() == EfNamespace)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsQueryable(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol self && IsType(self, "System.Linq", "IQueryable"))
        {
            return true;
        }

        foreach (var contract in type.AllInterfaces)
        {
            if (IsType(contract, "System.Linq", "IQueryable"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsType(INamedTypeSymbol type, string ns, string name) =>
        type.Name == name && type.ContainingNamespace.ToDisplayString() == ns;

    /// <summary>Removes parentheses, <c>!</c>, <c>await</c> and <c>ConfigureAwait(...)</c> around the materializing call.</summary>
    private static ExpressionSyntax Strip(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed:
                    expression = suppressed.Operand;
                    break;
                case AwaitExpressionSyntax awaited:
                    expression = awaited.Expression;
                    break;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configure }:
                    expression = configure.Expression;
                    break;
                default:
                    return expression;
            }
        }
    }
}
