using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.NullCoalescingAssignment;

/// <summary>NLR0004: suggests <c>??=</c> where an assignment only happens when the value is null.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullCoalescingAssignmentAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0004";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Use null-coalescing assignment",
        messageFormat: "Use '??=' to assign only when the value is null",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A variable, field or property that is given a value only while it is still null can use '??=' instead of an 'if' or a '??' expression that repeats its name.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0004.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IfStatement, SyntaxKind.ExpressionStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (NullCoalescingAssignmentAnalysis.Analyze(context.Node, context.SemanticModel, context.CancellationToken) is { } candidate)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, candidate.Anchor.GetLocation()));
        }
    }
}
