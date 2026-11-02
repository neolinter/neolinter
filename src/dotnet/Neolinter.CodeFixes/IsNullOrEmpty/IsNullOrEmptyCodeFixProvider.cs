using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.IsNullOrEmpty;

namespace Neolinter.CodeFixes.IsNullOrEmpty;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IsNullOrEmptyCodeFixProvider)), Shared]
public sealed class IsNullOrEmptyCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use 'string.IsNullOrEmpty'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(IsNullOrEmptyAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        // The diagnostic ends where the binary expression ends (it may start inside a longer chain).
        var node = root?.FindToken(context.Span.End - 1).Parent?.AncestorsAndSelf().OfType<BinaryExpressionSyntax>()
            .FirstOrDefault(b => b.Span.End == context.Span.End && (b.IsKind(SyntaxKind.LogicalOrExpression) || b.IsKind(SyntaxKind.LogicalAndExpression)));
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, node, ct), equivalenceKey: IsNullOrEmptyAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, BinaryExpressionSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || IsNullOrEmptyAnalysis.Analyze(node, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        ExpressionSyntax call = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)),
                SyntaxFactory.IdentifierName("IsNullOrEmpty")),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(candidate.Receiver.WithoutTrivia()))));
        if (candidate.Negate)
        {
            call = SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, call);
        }

        call = call.WithLeadingTrivia(candidate.First.GetLeadingTrivia()).WithTrailingTrivia(node.Right.GetTrailingTrivia());

        // The operands in front of the pair stay: 'a || s == null || s == ""' gives 'a || string.IsNullOrEmpty(s)'.
        SyntaxNode replacement = node.Left is BinaryExpressionSyntax left && left.IsKind(node.Kind()) ? left.WithRight(call) : call;
        return document.WithSyntaxRoot(root.ReplaceNode(node, replacement));
    }
}
