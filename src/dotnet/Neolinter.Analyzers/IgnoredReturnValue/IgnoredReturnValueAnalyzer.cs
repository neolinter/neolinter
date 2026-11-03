using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.IgnoredReturnValue;

/// <summary>NLR0030: calling a method that only computes a new value and dropping the result does nothing.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoredReturnValueAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0030";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Ignored return value",
        messageFormat: "The result of '{0}' is not used",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Strings, dates, immutable collections and LINQ operators never change in place; the call has no effect unless its result is used.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0030.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ExpressionStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var statement = (ExpressionStatementSyntax)context.Node;
        if (IgnoredReturnValueAnalysis.GetPureMethod(statement, context.SemanticModel, context.CancellationToken) is { } method)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, statement.Expression.GetLocation(), method.Name));
        }
    }
}
