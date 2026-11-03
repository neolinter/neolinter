using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.Rethrow;

namespace Neolinter.CodeFixes.Rethrow;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RethrowCodeFixProvider)), Shared]
public sealed class RethrowCodeFixProvider : CodeFixProvider
{
    private const string Title = "Rethrow with 'throw;'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(RethrowAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<ThrowStatementSyntax>().FirstOrDefault(t => t.Span == context.Span);
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, node, ct), equivalenceKey: RethrowAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, ThrowStatementSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || node.Expression is not { } expression || !RethrowAnalysis.IsReportable(node, model, cancellationToken))
        {
            return document;
        }

        // Trailing whitespace before ';' goes away; comments stay.
        var between = node.ThrowKeyword.TrailingTrivia
            .Concat(expression.GetLeadingTrivia())
            .Concat(expression.GetTrailingTrivia())
            .Concat(node.SemicolonToken.LeadingTrivia)
            .ToList();
        while (between.Count > 0 && between[between.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            between.RemoveAt(between.Count - 1);
        }

        var replacement = node
            .WithThrowKeyword(node.ThrowKeyword.WithTrailingTrivia(between))
            .WithExpression(null)
            .WithSemicolonToken(node.SemicolonToken.WithLeadingTrivia(SyntaxTriviaList.Empty));
        return document.WithSyntaxRoot(root.ReplaceNode(node, replacement));
    }
}
