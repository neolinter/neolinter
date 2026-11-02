using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.ElseAfterExit;

/// <summary>NLR0009: an <c>else</c> that follows a branch which always leaves is not needed.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ElseAfterExitAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0009";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Remove 'else' after a branch that exits",
        messageFormat: "This 'else' is not needed: the branch before it always leaves, so its statements can follow the 'if'",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "When the branch of an 'if' always leaves with return, throw, break, continue, goto or yield break, the code of the 'else' can follow the 'if' without it, one level less indented.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0009.yaml");

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
        if (ElseAfterExitAnalysis.Analyze(ifStatement, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, ifStatement.Else!.ElseKeyword.GetLocation()));
        }
    }
}
