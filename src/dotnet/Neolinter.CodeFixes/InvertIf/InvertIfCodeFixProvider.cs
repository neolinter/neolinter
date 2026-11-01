using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.InvertIf;

namespace Neolinter.CodeFixes.InvertIf;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InvertIfCodeFixProvider)), Shared]
public sealed class InvertIfCodeFixProvider : CodeFixProvider
{
    private const string Title = "Invert 'if' to reduce nesting";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(InvertIfAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindToken(context.Span.Start).Parent is not IfStatementSyntax ifStatement)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => InvertAsync(context.Document, root, ifStatement, ct), equivalenceKey: InvertIfAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> InvertAsync(Document document, SyntaxNode root, IfStatementSyntax ifStatement, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return document;
        }

        // The analyzer already applied the configured threshold.
        var candidate = InvertIfAnalysis.Analyze(ifStatement, model, minStatements: 1, cancellationToken);
        if (candidate is null)
        {
            return document;
        }

        var options = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(ifStatement.SyntaxTree);
        var rewritten = InvertIfRewriter.Rewrite(
            candidate,
            model,
            InvertIfOptions.GetSimplifyLogicalOperators(options),
            InvertIfOptions.GetGuardBraces(options),
            cancellationToken);
        return document.WithSyntaxRoot(root.ReplaceNode(candidate.Block, rewritten));
    }
}
