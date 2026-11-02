using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.StringInterpolation;

namespace Neolinter.CodeFixes.StringInterpolation;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StringInterpolationCodeFixProvider)), Shared]
public sealed class StringInterpolationCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use string interpolation";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(StringInterpolationAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<BinaryExpressionSyntax>().FirstOrDefault(b => b.Span == context.Span);
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, node, ct), equivalenceKey: StringInterpolationAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, BinaryExpressionSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || StringInterpolationAnalysis.Analyze(node, model, cancellationToken) is not { } text)
        {
            return document;
        }

        var replacement = SyntaxFactory.ParseExpression(text).WithTriviaFrom(node);
        return document.WithSyntaxRoot(root.ReplaceNode(node, replacement));
    }
}
