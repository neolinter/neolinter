using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.IsNull;

/// <summary>NLR0013: <c>is null</c> and <c>is not null</c> cannot be hijacked by an overloaded equality operator.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IsNullAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0013";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use 'is null' instead of comparing with 'null'",
        messageFormat: "Use 'is null' or 'is not null' to test for null",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "'x is null' and 'x is not null' always test for null, whatever '==' and '!=' are defined to do for the type of 'x'.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0013.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;
        if (IsNullAnalysis.Analyze(binary, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, binary.GetLocation()));
        }
    }
}
