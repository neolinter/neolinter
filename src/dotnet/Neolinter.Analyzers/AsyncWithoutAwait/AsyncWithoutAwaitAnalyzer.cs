using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.AsyncWithoutAwait;

/// <summary>NLR0027: an <c>async</c> function that never awaits pays for a state machine and does nothing asynchronous.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncWithoutAwaitAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0027";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Async method without await",
        messageFormat: "This async function never awaits: remove 'async' and return the task, or await something",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An async function without await runs synchronously and only adds a state machine; it is usually a missing await or an unneeded async.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0027.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement,
            SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (AsyncWithoutAwaitAnalysis.IsReportable(context.Node, context.SemanticModel, context.CancellationToken, out var location))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, location));
        }
    }
}
