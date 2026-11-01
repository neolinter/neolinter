using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.BooleanComparison;

/// <summary>NLR0005: a <c>bool</c> compared with <c>true</c> or <c>false</c> is the value or its negation.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BooleanComparisonAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0005";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Simplify comparison with a boolean literal",
        messageFormat: "Remove the comparison with a boolean literal",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A bool that is compared with 'true' or 'false' already is the answer: use the value, or its negation with '!'.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0005.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (BooleanComparisonAnalysis.Analyze((BinaryExpressionSyntax)context.Node, context.SemanticModel, context.CancellationToken) is { } candidate)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, candidate.Comparison.GetLocation()));
        }
    }
}
