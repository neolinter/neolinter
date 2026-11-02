using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.SwitchExpression;

/// <summary>NLR0019: a <c>switch</c> statement that only returns or assigns a value is a switch expression.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SwitchExpressionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0019";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use a switch expression",
        messageFormat: "Use a switch expression instead of this 'switch' statement",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A 'switch' statement whose sections only return a value, or only assign the same variable, reads better as a switch expression.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0019.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.SwitchStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = (SwitchStatementSyntax)context.Node;
        if (SwitchExpressionAnalysis.Analyze(node, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.SwitchKeyword.GetLocation()));
        }
    }
}
