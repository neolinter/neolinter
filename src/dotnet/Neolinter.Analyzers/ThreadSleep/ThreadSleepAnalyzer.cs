using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.ThreadSleep;

/// <summary>NLR0026: Thread.Sleep inside an async method holds a thread for nothing; await Task.Delay instead.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ThreadSleepAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0026";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use await Task.Delay instead of Thread.Sleep in async code",
        messageFormat: "Use 'await Task.Delay' instead of 'Thread.Sleep' in async code",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Thread.Sleep blocks the thread for the whole delay; await Task.Delay releases it while waiting.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0026.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (ThreadSleepAnalysis.IsSleep(invocation, context.SemanticModel, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
        }
    }
}
