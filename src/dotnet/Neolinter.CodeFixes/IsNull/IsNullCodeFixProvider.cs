using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.IsNull;

namespace Neolinter.CodeFixes.IsNull;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IsNullCodeFixProvider)), Shared]
public sealed class IsNullCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use 'is null'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(IsNullAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true) is not BinaryExpressionSyntax binary)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, binary, ct), equivalenceKey: IsNullAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, BinaryExpressionSyntax binary, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || IsNullAnalysis.Analyze(binary, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        PatternSyntax pattern = SyntaxFactory.ConstantPattern(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression));
        if (candidate.Negate)
        {
            pattern = SyntaxFactory.UnaryPattern(SyntaxFactory.Token(default, SyntaxKind.NotKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space)), pattern);
        }

        var test = SyntaxFactory.IsPatternExpression(
            candidate.Operand.WithoutTrivia(),
            SyntaxFactory.Token(SyntaxFactory.TriviaList(SyntaxFactory.Space), SyntaxKind.IsKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space)),
            pattern);

        return document.WithSyntaxRoot(root.ReplaceNode(binary, test.WithTriviaFrom(binary)));
    }
}
