using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.SelfAssignment;

namespace Neolinter.CodeFixes.SelfAssignment;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SelfAssignmentCodeFixProvider)), Shared]
public sealed class SelfAssignmentCodeFixProvider : CodeFixProvider
{
    private const string Title = "Remove the self-assignment";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(SelfAssignmentAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault(a => a.Span == context.Span);
        if (root is null || node is null || !SelfAssignmentAnalysis.IsRemovable(node))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(Remove(root, node.Parent!))), equivalenceKey: SelfAssignmentAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    // Comments above the statement stay; its own indentation goes, otherwise the next line would be indented twice.
    private static SyntaxNode Remove(SyntaxNode root, SyntaxNode statement)
    {
        var leading = statement.GetLeadingTrivia().ToList();
        while (leading.Count > 0 && leading[leading.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            leading.RemoveAt(leading.Count - 1);
        }

        var tracked = root.TrackNodes(statement);
        var trimmed = tracked.ReplaceNode(tracked.GetCurrentNode(statement)!, tracked.GetCurrentNode(statement)!.WithLeadingTrivia(leading));
        return trimmed.RemoveNode(trimmed.GetCurrentNode(statement)!, SyntaxRemoveOptions.KeepLeadingTrivia)!;
    }
}
