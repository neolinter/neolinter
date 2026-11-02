using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.ForeachWhere;

namespace Neolinter.CodeFixes.ForeachWhere;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ForeachTakeWhileCodeFixProvider)), Shared]
public sealed class ForeachTakeWhileCodeFixProvider : CodeFixProvider
{
    private const string Title = "Move the guard into 'TakeWhile'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ForeachTakeWhileAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<IfStatementSyntax>()
            .FirstOrDefault(s => s.Span == context.Span);
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ForeachWhereCodeFixProvider.ReplaceAsync(context.Document, root, node, breakGuards: true, ct), equivalenceKey: ForeachTakeWhileAnalyzer.DiagnosticId),
            context.Diagnostics);
    }
}
