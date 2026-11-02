using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.ExplicitBoolean;

/// <summary>NLR0010: a condition that is spelled out as <c>true</c> and <c>false</c> is the condition itself.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitBooleanAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0010";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use the condition instead of 'true' and 'false'",
        messageFormat: "The condition already is the result: 'true' and 'false' only restate it",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "'c ? true : false', 'if (c) return true; else return false;' and 'if (c) return true; return false;' return the value of a bool condition: write the condition (or its negation with '!') instead.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0010.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IfStatement, SyntaxKind.ConditionalExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (ExplicitBooleanAnalysis.Analyze(context.Node, context.SemanticModel, context.CancellationToken) is null)
        {
            return;
        }

        var location = context.Node is IfStatementSyntax ifStatement
            ? ifStatement.IfKeyword.GetLocation()
            : ((ConditionalExpressionSyntax)context.Node).QuestionToken.GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(Rule, location));
    }
}
