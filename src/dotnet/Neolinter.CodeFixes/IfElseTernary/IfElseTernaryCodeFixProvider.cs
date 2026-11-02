using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.IfElseTernary;

namespace Neolinter.CodeFixes.IfElseTernary;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IfElseTernaryCodeFixProvider)), Shared]
public sealed class IfElseTernaryCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use a conditional expression";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(IfElseTernaryAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindToken(context.Span.Start).Parent?.AncestorsAndSelf().OfType<IfStatementSyntax>()
            .FirstOrDefault(s => s.IfKeyword.Span == context.Span);
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, node, ct), equivalenceKey: IfElseTernaryAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, IfStatementSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || IfElseTernaryAnalysis.Analyze(node, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        // The new statement keeps the leading trivia of the 'if' and the trailing trivia of the last statement it replaces.
        var last = candidate.Following ?? (StatementSyntax)node;
        var statement = SyntaxFactory.ParseStatement(candidate.Replacement)
            .WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(last.GetTrailingTrivia());

        if (candidate.Following is null)
        {
            return document.WithSyntaxRoot(root.ReplaceNode(node, statement));
        }

        // The final 'return' goes away and the 'if' becomes the new return.
        return node.Parent switch
        {
            BlockSyntax block => document.WithSyntaxRoot(root.ReplaceNode(block, block.WithStatements(Merge(block.Statements, node, statement)))),
            SwitchSectionSyntax section => document.WithSyntaxRoot(root.ReplaceNode(section, section.WithStatements(Merge(section.Statements, node, statement)))),
            _ => document,
        };
    }

    private static SyntaxList<StatementSyntax> Merge(SyntaxList<StatementSyntax> statements, IfStatementSyntax ifStatement, StatementSyntax replacement)
    {
        var index = statements.IndexOf(ifStatement);
        return statements.Replace(ifStatement, replacement).RemoveAt(index + 1);
    }
}
