using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.StringFormat;

/// <summary>NLR0022: a call of <c>string.Format</c> with a literal format is an interpolated string.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringFormatAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0022";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use string interpolation instead of string.Format",
        messageFormat: "Use string interpolation instead of string.Format",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A string.Format call with a literal format and numbered placeholders is easier to read as an interpolated string.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0022.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = (InvocationExpressionSyntax)context.Node;
        if (StringFormatAnalysis.Analyze(node, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
        }
    }
}
