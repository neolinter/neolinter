using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.ForeachWhere;

/// <summary>NLR0018: leading <c>if (...) break;</c> guards of a <c>foreach</c> are a <c>TakeWhile</c> on its source.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ForeachTakeWhileAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0018";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Move the 'break' guard into 'TakeWhile'",
        messageFormat: "Move this 'break' guard into 'TakeWhile' on the 'foreach' source",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A guard that ends a 'foreach' at the start of its body can limit the source with 'TakeWhile' instead.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0018.yaml");

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
        if (ForeachWhereAnalysis.Analyze(node, breakGuards: true, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
        }
    }
}
