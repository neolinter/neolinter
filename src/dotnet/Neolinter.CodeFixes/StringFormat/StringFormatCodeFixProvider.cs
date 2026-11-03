using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.StringFormat;

namespace Neolinter.CodeFixes.StringFormat;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StringFormatCodeFixProvider)), Shared]
public sealed class StringFormatCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use string interpolation instead of string.Format";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(StringFormatAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault(i => i.Span == context.Span);
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, node, ct), equivalenceKey: StringFormatAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, InvocationExpressionSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || StringFormatAnalysis.Analyze(node, model, cancellationToken) is not { } text)
        {
            return document;
        }

        var replacement = SyntaxFactory.ParseExpression(text).WithTriviaFrom(node);
        return document.WithSyntaxRoot(root.ReplaceNode(node, replacement));
    }
}
