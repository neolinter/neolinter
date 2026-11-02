using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.ForeachWhere;

/// <summary>NLR0017: leading <c>if (...) continue;</c> guards of a <c>foreach</c> are a <c>Where</c> on its source.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForeachWhereAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0017";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Move the 'continue' guard into 'Where'",
        messageFormat: "Move this 'continue' guard into 'Where' on the 'foreach' source",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A guard that skips items at the start of a 'foreach' body can filter the source with 'Where' instead.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0017.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IfStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = (IfStatementSyntax)context.Node;
        if (ForeachWhereAnalysis.Analyze(node, breakGuards: false, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
        }
    }
}
