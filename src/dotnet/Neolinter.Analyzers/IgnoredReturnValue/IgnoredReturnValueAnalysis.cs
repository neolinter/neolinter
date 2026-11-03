using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.IgnoredReturnValue;

/// <summary>Detection shared by the NLR0030 analyzer and its code fix.</summary>
internal static class IgnoredReturnValueAnalysis
{
    private static readonly string[] ValueTypes = ["DateTime", "DateTimeOffset", "TimeSpan", "DateOnly", "TimeOnly"];

    /// <summary>The method called by an expression statement that only drops the result of a known pure method, else null.</summary>
    internal static IMethodSymbol? GetPureMethod(ExpressionStatementSyntax statement, SemanticModel model, CancellationToken token)
    {
        if (statement.Expression is not InvocationExpressionSyntax invocation
            || model.GetSymbolInfo(invocation, token).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        return !method.ReturnsVoid && IsPure(method) ? method : null;
    }

    private static bool IsPure(IMethodSymbol method)
    {
        // A method that writes through an out/ref parameter does more than return a value.
        if (method.Parameters.Any(p => p.RefKind is RefKind.Out or RefKind.Ref))
        {
            return false;
        }

        if (method.GetAttributes().Any(a => a.AttributeClass?.Name is "PureAttribute" or "MustUseReturnValueAttribute"))
        {
            return true;
        }

        var type = method.ContainingType;
        var ns = type.ContainingNamespace.ToDisplayString();
        return (ns, type.Name) switch
        {
            ("System", "String") => method.Name != "Intern",
            ("System", "Math" or "Convert") => true,
            ("System.IO", "Path") => method.Name != "GetTempFileName",
            ("System.Linq", "Enumerable" or "Queryable") => true,
            ("System", _) when ValueTypes.Contains(type.Name) => !method.IsStatic,
            ("System.Collections.Immutable", _) => !method.IsStatic && type.ContainingType is null,
            _ => false,
        };
    }

    /// <summary>
    /// <c>x.Method(...)</c> where x is a local, a parameter or a field that can be assigned and the call returns the type of x:
    /// <c>x = x.Method(...)</c> is then what was meant. Returns the receiver.
    /// </summary>
    internal static ExpressionSyntax? GetAssignableReceiver(ExpressionStatementSyntax statement, SemanticModel model, CancellationToken token)
    {
        if (statement.Parent is not BlockSyntax
            || statement.Expression is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: var receiver } }
            || GetPureMethod(statement, model, token) is not { IsExtensionMethod: false, IsStatic: false } method
            || !IsSimpleName(receiver)
            || model.GetTypeInfo(receiver, token).Type is not { } receiverType
            || !SymbolEqualityComparer.Default.Equals(receiverType, method.ReturnType))
        {
            return null;
        }

        return model.GetSymbolInfo(receiver, token).Symbol switch
        {
            ILocalSymbol { IsConst: false, IsRef: false } => receiver,
            IParameterSymbol { RefKind: RefKind.None } => receiver,
            IFieldSymbol { IsConst: false, IsReadOnly: false } => receiver,
            _ => null,
        };
    }

    private static bool IsSimpleName(ExpressionSyntax expression) => expression is IdentifierNameSyntax
        || expression is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax };
}
