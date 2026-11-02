using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Neolinter.Analyzers.MergeNestedIf;

namespace Neolinter.CodeFixes.MergeNestedIf;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MergeNestedIfCodeFixProvider)), Shared]
public sealed class MergeNestedIfCodeFixProvider : CodeFixProvider
{
    private const string Title = "Merge the nested 'if' statements with '&&'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(MergeNestedIfAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindToken(context.Span.Start).Parent is not IfStatementSyntax ifStatement)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => MergeAsync(context.Document, root, ifStatement, ct), equivalenceKey: MergeNestedIfAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> MergeAsync(Document document, SyntaxNode root, IfStatementSyntax outer, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || MergeNestedIfAnalysis.Analyze(outer, model, cancellationToken) is not { } chain)
        {
            return document;
        }

        var innermost = chain.Innermost;
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        // The conditions are joined in order, so '&&' still short-circuits like the nested 'if' statements did.
        var condition = Parenthesize(chain.Outer.Condition);
        foreach (var inner in chain.Ifs.Skip(1))
        {
            condition = And(condition, Parenthesize(inner.Condition.WithoutLeadingTrivia()));
        }

        // The inner body moves up by the indentation of the dropped levels; what the outer 'if' had after its last
        // token (the end of its closing brace's line) ends the new statement.
        var statement = Dedent(innermost.Statement, GetIndentation(chain.Outer, innermost, text));
        statement = statement.ReplaceToken(
            statement.GetLastToken(),
            statement.GetLastToken().WithTrailingTrivia(chain.Outer.GetLastToken().TrailingTrivia));

        var merged = chain.Outer
            .WithCondition(condition)
            .WithCloseParenToken(chain.Outer.CloseParenToken.WithTrailingTrivia(innermost.CloseParenToken.TrailingTrivia))
            .WithStatement(statement);

        return document.WithSyntaxRoot(root.ReplaceNode(chain.Outer, merged));
    }

    // '&&' is left-associative: 'a && (b && c)' is written 'a && b && c', which parses as '(a && b) && c'.
    private static ExpressionSyntax And(ExpressionSyntax left, ExpressionSyntax right) =>
        right is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.LogicalAndExpression)
            ? And(And(left, binary.Left), binary.Right)
            : SyntaxFactory.BinaryExpression(
                SyntaxKind.LogicalAndExpression,
                left.WithoutTrailingTrivia(),
                SyntaxFactory.Token(SyntaxFactory.TriviaList(SyntaxFactory.Space), SyntaxKind.AmpersandAmpersandToken, SyntaxFactory.TriviaList(SyntaxFactory.Space)),
                right.WithoutLeadingTrivia());

    // Only what binds looser than '&&' needs parentheses. Tokens carry explicit (empty) trivia, so the host keeps the layout.
    private static ExpressionSyntax Parenthesize(ExpressionSyntax condition) =>
        condition switch
        {
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalOrExpression) || binary.IsKind(SyntaxKind.CoalesceExpression) => Wrap(condition),
            ConditionalExpressionSyntax or AssignmentExpressionSyntax or AnonymousFunctionExpressionSyntax or SwitchExpressionSyntax
                or QueryExpressionSyntax or RangeExpressionSyntax or ThrowExpressionSyntax => Wrap(condition),
            _ => condition,
        };

    private static ExpressionSyntax Wrap(ExpressionSyntax expression) =>
        SyntaxFactory.ParenthesizedExpression(
                SyntaxFactory.Token(default, SyntaxKind.OpenParenToken, default),
                expression.WithoutTrivia(),
                SyntaxFactory.Token(default, SyntaxKind.CloseParenToken, default))
            .WithTriviaFrom(expression);

    /// <summary>
    /// The whitespace by which the innermost <c>if</c> is indented more than the outer one, when both start their line
    /// (an 'else if' counts through the indentation of its line). Empty when they share a line or the layout is odd.
    /// </summary>
    private static string GetIndentation(IfStatementSyntax outer, IfStatementSyntax innermost, SourceText text)
    {
        var outerLine = text.Lines.GetLineFromPosition(outer.IfKeyword.SpanStart);
        var innerLine = text.Lines.GetLineFromPosition(innermost.IfKeyword.SpanStart);
        if (outerLine.LineNumber == innerLine.LineNumber)
        {
            return "";
        }

        var outerIndent = LeadingWhitespace(text, outerLine);
        var innerIndent = LeadingWhitespace(text, innerLine);
        return innerLine.Start + innerIndent.Length == innermost.IfKeyword.SpanStart && innerIndent.StartsWith(outerIndent, StringComparison.Ordinal)
            ? innerIndent.Substring(outerIndent.Length)
            : "";
    }

    private static string LeadingWhitespace(SourceText text, TextLine line)
    {
        var length = 0;
        while (line.Start + length < line.End && text[line.Start + length] is ' ' or '\t')
        {
            length++;
        }

        return text.ToString(new TextSpan(line.Start, length));
    }

    // Only the whitespace that starts a line is touched, so strings and comments keep their text.
    private static StatementSyntax Dedent(StatementSyntax statement, string indentation)
    {
        if (indentation.Length == 0)
        {
            return statement;
        }

        return statement.ReplaceTokens(statement.DescendantTokens(), (original, _) =>
        {
            var leading = original.LeadingTrivia;
            var startsLine = original.GetPreviousToken().TrailingTrivia.LastOrDefault().IsKind(SyntaxKind.EndOfLineTrivia);
            var changed = false;
            var result = new List<SyntaxTrivia>(leading.Count);
            for (var i = 0; i < leading.Count; i++)
            {
                var trivia = leading[i];
                var atLineStart = i == 0 ? startsLine : leading[i - 1].IsKind(SyntaxKind.EndOfLineTrivia);
                if (atLineStart && trivia.IsKind(SyntaxKind.WhitespaceTrivia) && trivia.ToString().StartsWith(indentation, StringComparison.Ordinal))
                {
                    changed = true;
                    var rest = trivia.ToString().Substring(indentation.Length);
                    if (rest.Length > 0)
                    {
                        result.Add(SyntaxFactory.Whitespace(rest));
                    }
                }
                else
                {
                    result.Add(trivia);
                }
            }

            return changed ? original.WithLeadingTrivia(SyntaxFactory.TriviaList(result)) : original;
        });
    }
}
