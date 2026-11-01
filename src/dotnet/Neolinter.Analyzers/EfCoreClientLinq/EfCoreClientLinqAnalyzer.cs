using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.EfCoreClientLinq;

/// <summary>NLR0006: a LINQ operator applied in memory to a loaded Entity Framework Core query belongs inside the query.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EfCoreClientLinqAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0006";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Keep LINQ operators inside the Entity Framework Core query",
        messageFormat: "'{0}' runs in memory on the result of '{1}': apply it to the query before the data is loaded",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Loading a query and then filtering, projecting, sorting or counting the loaded objects fetches every row and column first. Put the operator before the loading call so the database does the work.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0006.yaml");

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
        if (EfCoreClientLinqAnalysis.TryFind(invocation, context.SemanticModel, context.CancellationToken, out var name, out var materializer))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), name.Identifier.ValueText, materializer));
        }
    }
}
