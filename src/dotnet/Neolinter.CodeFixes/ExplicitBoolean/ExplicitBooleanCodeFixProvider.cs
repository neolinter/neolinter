using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.ExplicitBoolean;

namespace Neolinter.CodeFixes.ExplicitBoolean;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExplicitBooleanCodeFixProvider)), Shared]
public sealed class ExplicitBooleanCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use the condition instead of 'true' and 'false'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ExplicitBooleanAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindToken(context.Span.Start).Parent?.AncestorsAndSelf().FirstOrDefault(n => n is IfStatementSyntax or ConditionalExpressionSyntax);
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, node, ct), equivalenceKey: ExplicitBooleanAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, SyntaxNode node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || ExplicitBooleanAnalysis.Analyze(node, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        var condition = candidate.Condition.WithoutTrivia();
        var value = candidate.Negate ? LogicalNot.Of(condition) : condition;

        if (node is ConditionalExpressionSyntax conditional)
        {
            return document.WithSyntaxRoot(root.ReplaceNode(conditional, value.WithTriviaFrom(conditional)));
        }

        // 'return value;' keeps the leading trivia of the 'if' and the trailing trivia of its last statement.
        var ifStatement = (IfStatementSyntax)node;
        var last = candidate.Following ?? (StatementSyntax)ifStatement;
        var returnStatement = SyntaxFactory.ReturnStatement(
            SyntaxFactory.Token(ifStatement.GetLeadingTrivia(), SyntaxKind.ReturnKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space)),
            value,
            SyntaxFactory.Token(default, SyntaxKind.SemicolonToken, last.GetTrailingTrivia()));

        if (candidate.Following is null)
        {
            return document.WithSyntaxRoot(root.ReplaceNode(ifStatement, returnStatement));
        }

        // The final 'return false;' goes away and the 'if' becomes the new return.
        return ifStatement.Parent switch
        {
            BlockSyntax block => document.WithSyntaxRoot(root.ReplaceNode(block, block.WithStatements(Merge(block.Statements, ifStatement, returnStatement)))),
            SwitchSectionSyntax section => document.WithSyntaxRoot(root.ReplaceNode(section, section.WithStatements(Merge(section.Statements, ifStatement, returnStatement)))),
            _ => document,
        };
    }

    private static SyntaxList<StatementSyntax> Merge(SyntaxList<StatementSyntax> statements, IfStatementSyntax ifStatement, StatementSyntax returnStatement)
    {
        var index = statements.IndexOf(ifStatement);
        return statements.Replace(ifStatement, returnStatement).RemoveAt(index + 1);
    }
}
