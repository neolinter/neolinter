using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.BlockingTask;

namespace Neolinter.Analyzers.ThreadSleep;

/// <summary>Detection shared by the NLR0026 analyzer and its code fix.</summary>
internal static class ThreadSleepAnalysis
{
    /// <summary><c>Thread.Sleep(int)</c> or <c>Thread.Sleep(TimeSpan)</c> inside an async function, other than a zero delay.</summary>
    internal static bool IsSleep(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken token)
    {
        if (model.GetSymbolInfo(invocation, token).Symbol is not IMethodSymbol { Name: "Sleep", IsStatic: true, ContainingType: { Name: "Thread" } type }
            || type.ContainingNamespace.ToDisplayString() != "System.Threading"
            || invocation.ArgumentList.Arguments.Count != 1
            || !BlockingTaskAnalysis.IsInAsyncFunction(invocation))
        {
            return false;
        }

        // Sleep(0) only yields the time slice, it is not a delay.
        var argument = invocation.ArgumentList.Arguments[0].Expression;
        return !(model.GetConstantValue(argument, token).Value is 0
            || model.GetSymbolInfo(argument, token).Symbol is IFieldSymbol { Name: "Zero", ContainingType.Name: "TimeSpan" });
    }
}
