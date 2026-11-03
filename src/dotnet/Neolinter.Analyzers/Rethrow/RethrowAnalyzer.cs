using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.Rethrow;

/// <summary>NLR0023: <c>throw e;</c> in a catch block resets the stack trace, <c>throw;</c> keeps it.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RethrowAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0023";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Rethrow with throw; instead of throw ex;",
        messageFormat: "Use 'throw;' to rethrow the caught exception and keep its stack trace",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Throwing the caught exception variable restarts its stack trace at the throw statement; a bare throw keeps the original one.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0023.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ThrowStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = (ThrowStatementSyntax)context.Node;
        if (RethrowAnalysis.IsReportable(node, context.SemanticModel, context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
        }
    }
}
