using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.PatternCast;

/// <summary>NLR0016: <c>as</c> followed by a null check, or <c>is</c> followed by a cast, is a type pattern with a variable.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PatternCastAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0016";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use a type pattern instead of a cast",
        messageFormat: "Use a type pattern ('is T name') instead of testing the type and casting",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "'is T name' tests the type and declares the variable in one step; 'as' with a null check, or 'is' followed by a cast, does it in two.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0016.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.AsExpression, SyntaxKind.IsExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var node = (BinaryExpressionSyntax)context.Node;
        if (PatternCastAnalysis.Analyze(node, context.SemanticModel, context.CancellationToken) is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
        }
    }
}
