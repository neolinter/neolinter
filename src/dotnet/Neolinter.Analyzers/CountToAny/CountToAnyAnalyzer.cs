using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.CountToAny;

/// <summary>NLR0011: asking whether a sequence has items does not need to count them.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CountToAnyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0011";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use 'Any()' instead of comparing 'Count()' with zero",
        messageFormat: "Use 'Any()' to ask whether the sequence has items: 'Count()' walks all of them",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Comparing the LINQ 'Count()' (with or without a predicate) with 0 or 1 only asks whether the sequence is empty: 'Any()' stops at the first item.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0011.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            Analyze,
            SyntaxKind.GreaterThanExpression,
            SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.LessThanExpression,
            SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.EqualsExpression,
            SyntaxKind.NotEqualsExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;
        if (CountToAnyAnalysis.Analyze(binary, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, binary.GetLocation()));
        }
    }
}
