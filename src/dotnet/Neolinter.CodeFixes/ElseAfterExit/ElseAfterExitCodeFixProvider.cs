using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.ElseAfterExit;
using Neolinter.Analyzers.InvertIf;

namespace Neolinter.CodeFixes.ElseAfterExit;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ElseAfterExitCodeFixProvider)), Shared]
public sealed class ElseAfterExitCodeFixProvider : CodeFixProvider
{
    private const string Title = "Remove the 'else' and keep its statements after the 'if'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ElseAfterExitAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindToken(context.Span.Start).Parent?.Parent is not IfStatementSyntax ifStatement)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => RemoveElseAsync(context.Document, root, ifStatement, ct), equivalenceKey: ElseAfterExitAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> RemoveElseAsync(Document document, SyntaxNode root, IfStatementSyntax ifStatement, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || ElseAfterExitAnalysis.Analyze(ifStatement, model, cancellationToken) is not { } block)
        {
            return document;
        }

        var elseClause = ifStatement.Else!;
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        // Tokens are rebuilt with explicit trivia (no elastic trivia): the host keeps the user's layout.
        var endOfLine = root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
        if (endOfLine == default)
        {
            endOfLine = SyntaxFactory.EndOfLine("\n");
        }

        var indentation = LineIndentation.Of(text, ifStatement.IfKeyword);
        var originals = InvertIfAnalysis.GetStatements(elseClause.Statement);
        var oneLevel = LineIndentation.Between(text, ifStatement.IfKeyword, originals[0].GetFirstToken());

        // The 'if' ends its line even when the 'else' used to follow it on that line ('} else {').
        var ifEnd = ifStatement.Statement.GetLastToken();
        var withoutElse = ifStatement.WithElse(null);
        if (!ElseAfterExitAnalysis.HasEndOfLine(ifEnd.TrailingTrivia))
        {
            withoutElse = withoutElse.ReplaceToken(withoutElse.GetLastToken(), withoutElse.GetLastToken().WithTrailingTrivia(endOfLine));
        }

        var moved = new List<StatementSyntax>(originals.Count);
        foreach (var original in originals)
        {
            moved.Add(LineIndentation.Dedent(original, oneLevel));
        }

        // The first statement starts a line at the indentation of the 'if'.
        var first = moved[0].GetFirstToken();
        if (!ElseAfterExitAnalysis.StartsLine(originals[0].GetFirstToken()))
        {
            moved[0] = moved[0].ReplaceToken(first, first.WithLeadingTrivia(indentation.Length == 0 ? default : SyntaxFactory.TriviaList(SyntaxFactory.Whitespace(indentation))));
        }

        // What ended the 'else' ends the last statement, so the code after the 'if' keeps its line.
        var lastIndex = moved.Count - 1;
        var last = moved[lastIndex].GetLastToken();
        if (!ElseAfterExitAnalysis.HasEndOfLine(last.TrailingTrivia))
        {
            var trailing = elseClause.Statement is BlockSyntax elseBlock ? elseBlock.CloseBraceToken.TrailingTrivia : SyntaxFactory.TriviaList(endOfLine);
            moved[lastIndex] = moved[lastIndex].ReplaceToken(last, last.WithTrailingTrivia(trailing));
        }

        var statements = block.Statements.Replace(ifStatement, withoutElse).InsertRange(block.Statements.IndexOf(ifStatement) + 1, moved);
        return document.WithSyntaxRoot(root.ReplaceNode(block, block.WithStatements(statements)));
    }
}
