using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.BlockingTask;

/// <summary>Detection shared by the NLR0025 analyzer and its code fix.</summary>
internal static class BlockingTaskAnalysis
{
    /// <summary>
    /// True for <c>t.Result</c>, <c>t.Wait(...)</c>, <c>t.GetAwaiter().GetResult()</c>, <c>Task.WaitAll(...)</c> and
    /// <c>Task.WaitAny(...)</c> inside an async function, on a task that is not known to be completed.
    /// <paramref name="awaited"/> is the expression to await instead, or null when there is no one-to-one replacement.
    /// </summary>
    internal static bool TryGetBlocking(ExpressionSyntax node, SemanticModel model, CancellationToken token, out ExpressionSyntax? awaited)
    {
        awaited = null;
        ExpressionSyntax? task;
        ExpressionSyntax? replacement = null;

        if (node is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Result" } access
            && node.IsKind(SyntaxKind.SimpleMemberAccessExpression)
            && model.GetSymbolInfo(access, token).Symbol is IPropertySymbol { IsStatic: false } property
            && IsTaskType(property.ContainingType))
        {
            task = replacement = access.Expression;
        }
        else if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation
            && model.GetSymbolInfo(invocation, token).Symbol is IMethodSymbol method)
        {
            switch (member.Name.Identifier.ValueText)
            {
                case "Wait" when !method.IsStatic && IsTaskType(method.ContainingType):
                    task = member.Expression;
                    if (invocation.ArgumentList.Arguments.Count == 0 && invocation.Parent is ExpressionStatementSyntax)
                    {
                        replacement = task;
                    }

                    break;
                case "WaitAll" or "WaitAny" when method.IsStatic && IsTaskType(method.ContainingType):
                    task = null;
                    break;
                case "GetResult" when IsInNamespace(method.ContainingType, "System.Runtime.CompilerServices")
                    && member.Expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetAwaiter" } getAwaiter, ArgumentList.Arguments.Count: 0 } call
                    && model.GetSymbolInfo(call, token).Symbol is IMethodSymbol { ContainingType: { } awaitable }
                    && (IsTaskType(awaitable) || IsConfiguredAwaitable(awaitable)):
                    task = replacement = getAwaiter.Expression;
                    break;
                default:
                    return false;
            }
        }
        else
        {
            return false;
        }

        if (!IsInAsyncFunction(node) || task is not null && IsCompleted(task, node, model, token))
        {
            return false;
        }

        awaited = replacement;
        return true;
    }

    /// <summary>False where <c>await</c> is not allowed, or would not compile around the expression.</summary>
    internal static bool CanAwait(SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        SyntaxNode? function = null;
        foreach (var ancestor in node.Ancestors())
        {
            if (function is null)
            {
                if (ancestor is LockStatementSyntax or CatchFilterClauseSyntax or QueryExpressionSyntax)
                {
                    return false;
                }

                if (IsFunction(ancestor))
                {
                    function = ancestor;
                }
            }

            if (IsUnsafe(ancestor))
            {
                return false;
            }
        }

        // A ref struct local cannot live across an await.
        return function is not null
            && !function.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Any(v => model.GetDeclaredSymbol(v, token) is ILocalSymbol { Type.IsRefLikeType: true });
    }

    private static bool IsUnsafe(SyntaxNode node) => node switch
    {
        UnsafeStatementSyntax => true,
        MethodDeclarationSyntax m => m.Modifiers.Any(SyntaxKind.UnsafeKeyword),
        LocalFunctionStatementSyntax l => l.Modifiers.Any(SyntaxKind.UnsafeKeyword),
        BaseTypeDeclarationSyntax t => t.Modifiers.Any(SyntaxKind.UnsafeKeyword),
        _ => false,
    };

    private static bool IsFunction(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax or AccessorDeclarationSyntax;

    private static SyntaxNode? GetFunction(SyntaxNode node) => node.Ancestors().FirstOrDefault(IsFunction);

    /// <summary>The innermost function is async: <c>await</c> is available there.</summary>
    private static bool IsInAsyncFunction(SyntaxNode node) => GetFunction(node) switch
    {
        MethodDeclarationSyntax m => m.Modifiers.Any(SyntaxKind.AsyncKeyword),
        LocalFunctionStatementSyntax l => l.Modifiers.Any(SyntaxKind.AsyncKeyword),
        AnonymousFunctionExpressionSyntax a => !a.AsyncKeyword.IsKind(SyntaxKind.None),
        _ => false,
    };

    /// <summary>
    /// A task that is done by construction (<c>Task.FromResult</c>, <c>Task.CompletedTask</c>), or a local or parameter
    /// awaited earlier in the same function, possibly through <c>Task.WhenAll</c> or <c>Task.WhenAny</c>.
    /// </summary>
    private static bool IsCompleted(ExpressionSyntax task, SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        task = Unwrap(task);
        switch (task)
        {
            case InvocationExpressionSyntax call:
                return model.GetSymbolInfo(call, token).Symbol is IMethodSymbol { IsStatic: true, Name: "FromResult" } m && IsTaskType(m.ContainingType);
            case MemberAccessExpressionSyntax access:
                return model.GetSymbolInfo(access, token).Symbol is IPropertySymbol { IsStatic: true, Name: "CompletedTask" } p && IsTaskType(p.ContainingType);
            case IdentifierNameSyntax name when model.GetSymbolInfo(name, token).Symbol is ILocalSymbol or IParameterSymbol:
                var symbol = model.GetSymbolInfo(name, token).Symbol!;
                return GetFunction(node) is { } function && IsAwaitedBefore(symbol, function, node, model, token);
            default:
                return false;
        }
    }

    private static bool IsAwaitedBefore(ISymbol symbol, SyntaxNode function, SyntaxNode node, SemanticModel model, CancellationToken token)
    {
        bool Is(IdentifierNameSyntax name) => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, token).Symbol, symbol);

        foreach (var awaitExpr in function.DescendantNodes().OfType<AwaitExpressionSyntax>())
        {
            if (awaitExpr.Span.End > node.SpanStart || GetFunction(awaitExpr) != function)
            {
                continue;
            }

            var awaited = Unwrap(awaitExpr.Expression);
            if (awaited is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configure })
            {
                awaited = Unwrap(configure.Expression);
            }

            if (awaited is IdentifierNameSyntax name)
            {
                if (Is(name))
                {
                    return true;
                }
            }
            else if (awaited is InvocationExpressionSyntax call
                && model.GetSymbolInfo(call, token).Symbol is IMethodSymbol { IsStatic: true, Name: "WhenAll" or "WhenAny" } whenAll
                && IsTaskType(whenAll.ContainingType)
                && call.ArgumentList.DescendantNodes().OfType<IdentifierNameSyntax>().Any(Is))
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool IsTaskType(INamedTypeSymbol? type) =>
        type is { Name: "Task" or "ValueTask" } && IsInNamespace(type, "System.Threading.Tasks");

    private static bool IsConfiguredAwaitable(INamedTypeSymbol type) =>
        type is { Name: "ConfiguredTaskAwaitable" or "ConfiguredValueTaskAwaitable" } && IsInNamespace(type, "System.Runtime.CompilerServices");

    private static bool IsInNamespace(INamedTypeSymbol? type, string name) => type?.ContainingNamespace.ToDisplayString() == name;
}
