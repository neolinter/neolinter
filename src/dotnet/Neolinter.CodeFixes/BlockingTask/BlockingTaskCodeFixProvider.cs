using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.BlockingTask;

namespace Neolinter.CodeFixes.BlockingTask;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlockingTaskCodeFixProvider)), Shared]
public sealed class BlockingTaskCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use 'await'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(BlockingTaskAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        foreach (var candidate in root.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<ExpressionSyntax>().Where(e => e.Span == context.Span))
        {
            if (!BlockingTaskAnalysis.TryGetBlocking(candidate, model, context.CancellationToken, out var awaited))
            {
                continue;
            }

            // WaitAll, WaitAny and Wait(timeout) have no one-to-one replacement; a lock or similar forbids await.
            if (awaited is not null && BlockingTaskAnalysis.CanAwait(candidate, model, context.CancellationToken))
            {
                context.RegisterCodeFix(
                    CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(candidate, Await(candidate, awaited)))), equivalenceKey: BlockingTaskAnalyzer.DiagnosticId),
                    context.Diagnostics);
            }

            return;
        }
    }

    private static ExpressionSyntax Await(ExpressionSyntax node, ExpressionSyntax awaited)
    {
        ExpressionSyntax result = SyntaxFactory.AwaitExpression(
            SyntaxFactory.Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            awaited.WithoutTrivia());

        // 'await' binds looser than member access, indexing, invocation and postfix operators.
        if (IsOperand(node))
        {
            result = SyntaxFactory.ParenthesizedExpression(result);
        }

        return result.WithLeadingTrivia(node.GetLeadingTrivia()).WithTrailingTrivia(node.GetTrailingTrivia());
    }

    /// <summary>True when the node is the expression being accessed, indexed, invoked or suffixed, not an argument or a name.</summary>
    private static bool IsOperand(ExpressionSyntax node) => node.Parent switch
    {
        MemberAccessExpressionSyntax m => m.Expression == node,
        ElementAccessExpressionSyntax e => e.Expression == node,
        InvocationExpressionSyntax i => i.Expression == node,
        ConditionalAccessExpressionSyntax c => c.Expression == node,
        PostfixUnaryExpressionSyntax => true,
        _ => false,
    };
}
