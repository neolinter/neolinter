using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Neolinter.Analyzers.IsNullOrEmpty;

/// <summary>NLR0014: a null test and an empty test on the same string are what <c>string.IsNullOrEmpty</c> says.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IsNullOrEmptyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0014";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use 'string.IsNullOrEmpty'",
        messageFormat: "Use 'string.IsNullOrEmpty' instead of testing for null and for an empty string",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "'string.IsNullOrEmpty(s)' says in one call what a null test and an empty test on 's' say together.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0014.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.LogicalOrExpression, SyntaxKind.LogicalAndExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = (BinaryExpressionSyntax)context.Node;
        if (IsNullOrEmptyAnalysis.Analyze(node, context.SemanticModel, context.CancellationToken) is { } candidate)
        {
            var span = TextSpan.FromBounds(candidate.First.SpanStart, node.Span.End);
            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(node.SyntaxTree, span)));
        }
    }
}
