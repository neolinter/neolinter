using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.MergeNestedIf;

/// <summary>NLR0008: an <c>if</c> without <c>else</c> that only contains another <c>if</c> without <c>else</c>.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MergeNestedIfAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0008";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Merge nested 'if' statements",
        messageFormat: "Join this condition and the one of the 'if' nested inside it with '&&'",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "When an 'if' without 'else' only contains another 'if' without 'else', one 'if' with both conditions joined by '&&' does the same with one level less.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0008.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IfStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;
        if (MergeNestedIfAnalysis.Analyze(ifStatement, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, ifStatement.IfKeyword.GetLocation()));
        }
    }
}
