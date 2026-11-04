using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.UselessCatch;

namespace Neolinter.CodeFixes.UselessCatch;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UselessCatchCodeFixProvider)), Shared]
public sealed class UselessCatchCodeFixProvider : CodeFixProvider
{
    private const string Title = "Remove the useless catch";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(UselessCatchAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var clause = root?.FindToken(context.Span.Start).Parent?.AncestorsAndSelf().OfType<CatchClauseSyntax>().FirstOrDefault();
        if (root is null || model is null || clause?.Parent is not TryStatementSyntax tryStatement
            || !UselessCatchAnalysis.IsUseless(clause, model, context.CancellationToken))
        {
            return;
        }

        // When every clause is useless and nothing else follows, each fix replaces the whole try by its block (identical edits merge in fix all).
        var unwrap = tryStatement.Finally is null
            && tryStatement.Catches.All(c => UselessCatchAnalysis.IsUseless(c, model, context.CancellationToken));
        SyntaxNode replacement = unwrap
            ? tryStatement.Block.WithLeadingTrivia(tryStatement.GetLeadingTrivia()).WithTrailingTrivia(tryStatement.GetTrailingTrivia())
            : tryStatement.WithCatches(tryStatement.Catches.Remove(clause));

        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(tryStatement, replacement))), equivalenceKey: UselessCatchAnalyzer.DiagnosticId),
            context.Diagnostics);
    }
}
