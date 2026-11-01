using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.NestedTernary;

/// <summary>NLR0007: a conditional expression that contains another conditional expression.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NestedTernaryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0007";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Avoid nested conditional expressions",
        messageFormat: "This conditional expression contains another one: spell the cases out with 'if' / 'else' or a 'switch'",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A '? :' inside another '? :' forces the reader to work out which branch belongs to which condition.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0007.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ConditionalExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var conditional = (ConditionalExpressionSyntax)context.Node;
        if (NestedTernaryAnalysis.IsReported(conditional))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, conditional.GetLocation()));
        }
    }
}
