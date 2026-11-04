using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.UselessCatch;

/// <summary>NLR0037: a <c>catch</c> that only rethrows with <c>throw;</c> does nothing.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UselessCatchAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0037";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Useless catch that only rethrows",
        messageFormat: "This 'catch' only rethrows the exception: remove it",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A catch clause whose body is only 'throw;' has no effect unless a later clause relies on it; remove it.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0037.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.CatchClause);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var clause = (CatchClauseSyntax)context.Node;
        if (UselessCatchAnalysis.IsUseless(clause, context.SemanticModel, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, clause.CatchKeyword.GetLocation()));
        }
    }
}
