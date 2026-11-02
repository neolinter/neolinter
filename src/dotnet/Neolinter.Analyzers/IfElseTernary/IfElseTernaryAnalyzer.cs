using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.IfElseTernary;

/// <summary>NLR0020: an <c>if</c>/<c>else</c> that only assigns the same variable or returns is a conditional expression.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IfElseTernaryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0020";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use a conditional expression",
        messageFormat: "Use a conditional expression instead of this 'if' statement",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "An 'if' and 'else' that only assign the same variable, or only return a value, read better as a conditional expression.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0020.yaml");

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
        if (IfElseTernaryAnalysis.Analyze(node, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.IfKeyword.GetLocation()));
        }
    }
}
