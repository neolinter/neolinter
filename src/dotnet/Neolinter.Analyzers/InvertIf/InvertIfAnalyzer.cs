using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.InvertIf;

/// <summary>NLR0001: suggests inverting an <c>if</c> to leave early and reduce nesting.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InvertIfAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0001";
    public const string MinStatementsOption = "nlr0001.min_statements";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Invert 'if' to reduce nesting",
        messageFormat: "Invert this 'if' and leave early to reduce nesting",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "When the rest of a block sits inside an 'if' and the other path only leaves the block, inverting the condition and leaving first keeps the main code one level less indented.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0001.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeIf, SyntaxKind.IfStatement);
    }

    private static void AnalyzeIf(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;
        var minStatements = GetMinStatements(context.Options.AnalyzerConfigOptionsProvider.GetOptions(ifStatement.SyntaxTree));

        if (InvertIfAnalysis.Analyze(ifStatement, context.SemanticModel, minStatements, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, ifStatement.IfKeyword.GetLocation()));
        }
    }

    // Invalid or out-of-range values fall back to the default instead of disabling the rule.
    internal static int GetMinStatements(AnalyzerConfigOptions options) =>
        options.TryGetValue(MinStatementsOption, out var value)
        && int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        && parsed >= 1
            ? parsed
            : InvertIfAnalysis.DefaultMinStatements;
}
