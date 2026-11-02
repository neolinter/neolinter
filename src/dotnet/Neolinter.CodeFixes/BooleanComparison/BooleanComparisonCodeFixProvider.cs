using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.BooleanComparison;

namespace Neolinter.CodeFixes.BooleanComparison;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BooleanComparisonCodeFixProvider)), Shared]
public sealed class BooleanComparisonCodeFixProvider : CodeFixProvider
{
    private const string Title = "Remove the comparison with a boolean literal";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(BooleanComparisonAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span) is not BinaryExpressionSyntax comparison)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, comparison, ct), equivalenceKey: BooleanComparisonAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, BinaryExpressionSyntax comparison, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || BooleanComparisonAnalysis.Analyze(comparison, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        var operand = candidate.Operand.WithoutTrivia();
        var replacement = candidate.Negate ? LogicalNot.Of(operand) : operand;

        return document.WithSyntaxRoot(root.ReplaceNode(comparison, replacement.WithTriviaFrom(comparison)));
    }
}
