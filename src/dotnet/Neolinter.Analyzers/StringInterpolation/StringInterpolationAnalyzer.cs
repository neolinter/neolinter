using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.StringInterpolation;

/// <summary>NLR0021: a concatenation of string literals and values is an interpolated string.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringInterpolationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0021";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use string interpolation",
        messageFormat: "Use string interpolation instead of this concatenation",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A chain of '+' that mixes string literals and other values is easier to read as an interpolated string.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0021.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.AddExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = (BinaryExpressionSyntax)context.Node;
        if (StringInterpolationAnalysis.Analyze(node, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
        }
    }
}
