using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Neolinter.CodeFixes;

/// <summary>Indentation helpers for code fixes that move statements up or down one level.</summary>
internal static class LineIndentation
{
    /// <summary>
    /// The whitespace by which the line of <paramref name="inner"/> is indented more than the line of
    /// <paramref name="outer"/>, when both tokens start their line. Empty when they share a line or the layout is odd.
    /// </summary>
    public static string Between(SourceText text, SyntaxToken outer, SyntaxToken inner)
    {
        var outerLine = text.Lines.GetLineFromPosition(outer.SpanStart);
        var innerLine = text.Lines.GetLineFromPosition(inner.SpanStart);
        if (outerLine.LineNumber == innerLine.LineNumber)
        {
            return "";
        }

        var outerIndent = LeadingWhitespace(text, outerLine);
        var innerIndent = LeadingWhitespace(text, innerLine);
        return innerLine.Start + innerIndent.Length == inner.SpanStart && innerIndent.StartsWith(outerIndent, StringComparison.Ordinal)
            ? innerIndent.Substring(outerIndent.Length)
            : "";
    }

    public static string Of(SourceText text, SyntaxToken token) =>
        LeadingWhitespace(text, text.Lines.GetLineFromPosition(token.SpanStart));

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
    public static StatementSyntax Dedent(StatementSyntax statement, string indentation)
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
