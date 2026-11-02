using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.WhereTerminal;

/// <summary>NLR0012: the predicate of 'Where' can be given to the LINQ operator that follows it.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WhereTerminalAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0012";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Fold 'Where' into the following LINQ operator",
        messageFormat: "Pass the predicate of 'Where' to '{0}' directly",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "'Where(p)' followed by 'Any', 'First', 'FirstOrDefault', 'Single', 'SingleOrDefault', 'Last', 'LastOrDefault', 'Count' or 'LongCount' without argument can give 'p' to that operator instead.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0012.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (WhereTerminalAnalysis.Analyze(invocation, context.SemanticModel, context.CancellationToken) is { } candidate)
        {
            var terminalName = ((MemberAccessExpressionSyntax)candidate.Terminal.Expression).Name.Identifier.ValueText;
            var whereName = ((MemberAccessExpressionSyntax)candidate.Where.Expression).Name;
            context.ReportDiagnostic(Diagnostic.Create(Rule, whereName.GetLocation(), terminalName));
        }
    }
}
