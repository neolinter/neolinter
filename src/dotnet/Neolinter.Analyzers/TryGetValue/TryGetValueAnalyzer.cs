using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.TryGetValue;

/// <summary>NLR0015: <c>ContainsKey(k)</c> followed by <c>d[k]</c> looks the key up twice; <c>TryGetValue</c> does it once.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TryGetValueAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0015";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use 'TryGetValue' instead of 'ContainsKey' and the indexer",
        messageFormat: "Use 'TryGetValue' instead of 'ContainsKey' followed by the indexer",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "'ContainsKey' followed by the indexer with the same key looks the key up twice; 'TryGetValue' looks it up once.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0015.yaml");

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
        if (TryGetValueAnalysis.Analyze(invocation, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
        }
    }
}
