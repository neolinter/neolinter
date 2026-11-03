using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.SelfAssignment;

/// <summary>NLR0029: assigning a variable or member to itself does nothing and usually hides a typo.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SelfAssignmentAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0029";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Self-assignment",
        messageFormat: "'{0}' is assigned to itself",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Assigning a variable or member to itself has no effect; it is usually a typo for a different name or a forgotten 'this.'.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0029.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = (AssignmentExpressionSyntax)context.Node;
        if (SelfAssignmentAnalysis.IsReportable(node, context.SemanticModel, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), node.Left.ToString()));
        }
    }
}
