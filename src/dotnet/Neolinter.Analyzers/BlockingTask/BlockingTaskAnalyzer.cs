using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.BlockingTask;

/// <summary>NLR0025: blocking on a task inside an async method can deadlock and holds a thread for nothing.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlockingTaskAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0025";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Do not block on a task inside an async method",
        messageFormat: "Do not block on a task inside an async method: use 'await' instead",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reading Result, calling Wait or GetResult, or using Task.WaitAll and Task.WaitAny blocks the thread, which can deadlock when a synchronization context is present; await the task instead.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0025.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.SimpleMemberAccessExpression, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = (ExpressionSyntax)context.Node;
        if (BlockingTaskAnalysis.TryGetBlocking(node, context.SemanticModel, context.CancellationToken, out _))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
        }
    }
}
