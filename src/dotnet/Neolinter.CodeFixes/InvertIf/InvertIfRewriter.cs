using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Neolinter.Analyzers.InvertIf;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Neolinter.CodeFixes.InvertIf;

/// <summary>
/// Rewrites the block containing the <c>if</c>: inverted <c>if</c> holding the exit, then the former
/// body one level up. Indentation, comments and brace placement of the original code are kept.
/// </summary>
internal static class InvertIfRewriter
{
    private const string DefaultIndentUnit = "    ";

    /// <param name="simplifyLogicalOperators">Option <c>nlr0001.simplify_logical_operators</c>.</param>
    /// <param name="guardBraces">Option <c>nlr0001.guard_braces</c>: <c>true</c> for <c>always</c>, <c>false</c> for <c>never</c>.</param>
    public static BlockSyntax Rewrite(InvertIfCandidate candidate, SemanticModel model, bool simplifyLogicalOperators, bool guardBraces, CancellationToken cancellationToken)
    {
        var ifStatement = candidate.IfStatement;
        var block = candidate.Block;
        var text = ifStatement.SyntaxTree.GetText(cancellationToken);
        var eol = EndOfLine(DetectNewLine(text));
        var ifIndent = IndentationOf(ifStatement.IfKeyword, text);

        var body = InvertIfAnalysis.GetStatements(ifStatement.Statement);
        var bodyIndent = body.Count > 0 && StartsLine(body[0].GetFirstToken(), text) ? IndentationOf(body[0].GetFirstToken(), text) : null;
        var unit = bodyIndent is not null && bodyIndent.Length > ifIndent.Length && bodyIndent.StartsWith(ifIndent, StringComparison.Ordinal)
            ? bodyIndent.Substring(ifIndent.Length)
            : DefaultIndentUnit;

        if (candidate.Kind == InvertIfKind.ElseIfCascade)
        {
            return RewriteCascade(candidate, model, simplifyLogicalOperators, guardBraces, text, eol, ifIndent, unit, cancellationToken);
        }

        var dedenter = new Dedenter(text, unit);

        var hoisted = body.Select(s => (StatementSyntax)dedenter.Visit(s)).ToList();
        if (bodyIndent is null)
        {
            hoisted[0] = hoisted[0].WithLeadingTrivia(Indent(ifIndent));
        }

        // Comments that end up after the hoisted statements.
        var after = new List<SyntaxTrivia>();

        if (ifStatement.Statement is BlockSyntax ifBlock)
        {
            if (HasComment(ifBlock.OpenBraceToken.TrailingTrivia))
            {
                hoisted[0] = hoisted[0].WithLeadingTrivia(
                    Indent(ifIndent).Concat(ifBlock.OpenBraceToken.TrailingTrivia.SkipWhile(IsWhitespace)).Concat(hoisted[0].GetLeadingTrivia()));
            }

            if (HasComment(ifBlock.CloseBraceToken.LeadingTrivia))
            {
                after.AddRange(UpToLastLine(ifBlock.CloseBraceToken.LeadingTrivia).Select(dedenter.VisitTrivia));
            }

            if (HasComment(ifBlock.CloseBraceToken.TrailingTrivia))
            {
                var last = hoisted[hoisted.Count - 1];
                hoisted[hoisted.Count - 1] = last.WithTrailingTrivia(
                    TrimEnd(last.GetTrailingTrivia()).Concat(EnsureEndOfLine(ifBlock.CloseBraceToken.TrailingTrivia, eol)));
            }
        }

        if (ifStatement.Else is { } elseClause)
        {
            if (HasComment(elseClause.ElseKeyword.LeadingTrivia))
            {
                after.AddRange(UpToLastLine(elseClause.ElseKeyword.LeadingTrivia));
            }

            if (HasComment(elseClause.ElseKeyword.TrailingTrivia))
            {
                after.AddRange(Indent(ifIndent).Concat(EnsureEndOfLine(TriviaList(elseClause.ElseKeyword.TrailingTrivia.SkipWhile(IsWhitespace)), eol)));
            }
        }

        var guard = BuildGuard(candidate, ConditionNegator.Negate(ifStatement.Condition, model, simplifyLogicalOperators, cancellationToken), text, ifIndent, unit, eol, guardBraces);

        if (!hoisted[0].GetLeadingTrivia().FirstOrDefault().IsKind(SyntaxKind.EndOfLineTrivia))
        {
            hoisted[0] = hoisted[0].WithLeadingTrivia(hoisted[0].GetLeadingTrivia().Insert(0, eol));
        }

        var index = block.Statements.IndexOf(ifStatement);
        var following = block.Statements.Skip(index + 1).ToList();
        var closeBrace = block.CloseBraceToken;
        if (after.Count > 0)
        {
            if (following.Count > 0)
            {
                following[0] = following[0].WithLeadingTrivia(after.Concat(following[0].GetLeadingTrivia()));
            }
            else
            {
                closeBrace = closeBrace.WithLeadingTrivia(after.Concat(closeBrace.LeadingTrivia));
            }
        }

        var statements = block.Statements.Take(index).Append(guard).Concat(hoisted).Concat(following);
        return block.WithStatements(List(statements)).WithCloseBraceToken(closeBrace);
    }

    /// <summary>
    /// <c>if (c1) B1 else if (c2) B2 else EXIT</c> becomes the guard <c>if (!c1 &amp;&amp; !c2) EXIT</c> followed by
    /// <c>if (c1) B1 else B2</c>: the branches stay where they are, only the final <c>else</c> moves up.
    /// </summary>
    private static BlockSyntax RewriteCascade(
        InvertIfCandidate candidate, SemanticModel model, bool simplifyLogicalOperators, bool guardBraces,
        SourceText text, SyntaxTrivia eol, string ifIndent, string unit, CancellationToken cancellationToken)
    {
        var head = candidate.IfStatement;
        var block = candidate.Block;

        ExpressionSyntax? condition = null;
        for (var current = head; ; current = (IfStatementSyntax)current.Else!.Statement)
        {
            var negated = ConditionNegator.ParenthesizeFor(
                ConditionNegator.Negate(current.Condition, model, simplifyLogicalOperators, cancellationToken).WithoutTrivia(),
                SyntaxKind.LogicalAndExpression);
            condition = condition is null
                ? negated
                : BinaryExpression(SyntaxKind.LogicalAndExpression, condition, Token(TriviaList(Space), SyntaxKind.AmpersandAmpersandToken, TriviaList(Space)), negated);

            if (current.Else!.Statement is not IfStatementSyntax)
            {
                break;
            }
        }

        var guard = BuildGuard(candidate, condition!.WithTriviaFrom(head.Condition), text, ifIndent, unit, eol, guardBraces);
        var remainder = DropFinalElse(head)
            .WithLeadingTrivia(TriviaList(eol).AddRange(Indent(ifIndent)))
            .WithTrailingTrivia(head.GetTrailingTrivia());

        var index = block.Statements.IndexOf(head);
        var statements = block.Statements.Take(index).Append(guard).Append(remainder).Concat(block.Statements.Skip(index + 1));
        return block.WithStatements(List(statements));
    }

    // The last "else if (c) B" becomes "else B": its condition is known to hold once the guard has passed.
    private static IfStatementSyntax DropFinalElse(IfStatementSyntax ifStatement)
    {
        var elseClause = ifStatement.Else!;
        var next = (IfStatementSyntax)elseClause.Statement;
        if (next.Else!.Statement is IfStatementSyntax)
        {
            return ifStatement.WithElse(elseClause.WithStatement(DropFinalElse(next)));
        }

        return ifStatement.WithElse(elseClause
            .WithElseKeyword(elseClause.ElseKeyword.WithTrailingTrivia(next.CloseParenToken.TrailingTrivia))
            .WithStatement(next.Statement));
    }

    private static IfStatementSyntax BuildGuard(
        InvertIfCandidate candidate, ExpressionSyntax condition, SourceText text, string ifIndent, string unit, SyntaxTrivia eol, bool guardBraces)
    {
        var ifStatement = candidate.IfStatement;
        var useBraces = guardBraces || NeedsBraces(candidate);
        var sameLineBrace = useBraces && ifStatement.Statement is BlockSyntax b && LineOf(b.OpenBraceToken, text) == LineOf(ifStatement.CloseParenToken, text);
        var closeParenTrailing = sameLineBrace
            ? TriviaList(Space)
            : HasComment(ifStatement.CloseParenToken.TrailingTrivia)
                ? EnsureEndOfLine(ifStatement.CloseParenToken.TrailingTrivia, eol)
                : TriviaList(eol);

        return ifStatement
            .WithCondition(condition)
            .WithCloseParenToken(ifStatement.CloseParenToken.WithTrailingTrivia(closeParenTrailing))
            .WithStatement(GuardBody(candidate, text, ifIndent, unit, sameLineBrace, eol, useBraces))
            .WithElse(null);
    }

    // Braces of the old else block that carry comments stay, so that no comment is lost.
    private static bool NeedsBraces(InvertIfCandidate candidate) =>
        candidate.Kind is InvertIfKind.ElseExit or InvertIfKind.ElseIfCascade
        && candidate.Exit is BlockSyntax elseBlock
        && (HasComment(elseBlock.OpenBraceToken.TrailingTrivia) || HasComment(elseBlock.CloseBraceToken.LeadingTrivia));

    private static StatementSyntax GuardBody(InvertIfCandidate candidate, SourceText text, string ifIndent, string unit, bool sameLineBrace, SyntaxTrivia eol, bool useBraces)
    {
        var innerIndent = ifIndent + unit;
        IEnumerable<SyntaxTrivia> closeLeading = Indent(ifIndent);
        var openTrailing = TriviaList(eol);
        IReadOnlyList<StatementSyntax> exits;

        if (candidate.Kind is InvertIfKind.ElseExit or InvertIfKind.ElseIfCascade)
        {
            var elseStatement = candidate.Exit;
            exits = InvertIfAnalysis.GetStatements(elseStatement);
            if (elseStatement is BlockSyntax elseBlock)
            {
                if (HasComment(elseBlock.OpenBraceToken.TrailingTrivia))
                {
                    openTrailing = EnsureEndOfLine(elseBlock.OpenBraceToken.TrailingTrivia, eol);
                }

                if (HasComment(elseBlock.CloseBraceToken.LeadingTrivia))
                {
                    closeLeading = UpToLastLine(elseBlock.CloseBraceToken.LeadingTrivia).Concat(closeLeading);
                }
            }
        }
        else
        {
            exits = new[] { candidate.Exit.WithoutTrivia() };
        }

        var normalized = exits.Select(exit =>
        {
            if (!exit.GetFirstToken().Span.IsEmpty && exit.SyntaxTree == candidate.IfStatement.SyntaxTree && StartsLine(exit.GetFirstToken(), text))
            {
                return exit.WithTrailingTrivia(EnsureEndOfLine(exit.GetTrailingTrivia(), eol));
            }

            return exit.WithLeadingTrivia(Indent(innerIndent)).WithTrailingTrivia(EnsureEndOfLine(TrimEnd(exit.GetTrailingTrivia()), eol));
        });

        if (!useBraces)
        {
            return normalized.Single();
        }

        return Block(
            Token(sameLineBrace ? TriviaList() : TriviaList(Indent(ifIndent)), SyntaxKind.OpenBraceToken, openTrailing),
            List(normalized),
            Token(TriviaList(closeLeading), SyntaxKind.CloseBraceToken, TriviaList(eol)));
    }

    private static string DetectNewLine(SourceText text)
    {
        foreach (var line in text.Lines)
        {
            if (line.EndIncludingLineBreak > line.End)
            {
                return text.ToString(TextSpan.FromBounds(line.End, line.EndIncludingLineBreak));
            }
        }

        return "\n";
    }

    private static string IndentationOf(SyntaxToken token, SourceText text)
    {
        var line = text.Lines.GetLineFromPosition(token.SpanStart);
        var end = line.Start;
        while (end < line.End && text[end] is ' ' or '\t')
        {
            end++;
        }

        return text.ToString(TextSpan.FromBounds(line.Start, end));
    }

    private static bool StartsLine(SyntaxToken token, SourceText text)
    {
        var line = text.Lines.GetLineFromPosition(token.SpanStart);
        return string.IsNullOrWhiteSpace(text.ToString(TextSpan.FromBounds(line.Start, token.SpanStart)));
    }

    private static int LineOf(SyntaxToken token, SourceText text) => text.Lines.GetLineFromPosition(token.SpanStart).LineNumber;

    private static SyntaxTriviaList Indent(string indentation) =>
        indentation.Length == 0 ? TriviaList() : TriviaList(Whitespace(indentation));

    private static bool HasComment(SyntaxTriviaList trivia) =>
        trivia.Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia));

    private static bool IsWhitespace(SyntaxTrivia trivia) => trivia.IsKind(SyntaxKind.WhitespaceTrivia);

    /// <summary>Full lines only: drops the indentation of the token that followed.</summary>
    private static IEnumerable<SyntaxTrivia> UpToLastLine(SyntaxTriviaList trivia)
    {
        var last = -1;
        for (var i = 0; i < trivia.Count; i++)
        {
            if (trivia[i].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                last = i;
            }
        }

        return trivia.Take(last + 1);
    }

    private static SyntaxTriviaList TrimEnd(SyntaxTriviaList trivia) =>
        TriviaList(trivia.Reverse().SkipWhile(t => t.IsKind(SyntaxKind.EndOfLineTrivia) || IsWhitespace(t)).Reverse());

    private static SyntaxTriviaList EnsureEndOfLine(SyntaxTriviaList trivia, SyntaxTrivia eol) =>
        trivia.Count > 0 && trivia[trivia.Count - 1].IsKind(SyntaxKind.EndOfLineTrivia) ? trivia : TrimEnd(trivia).Add(eol);

    /// <summary>Removes one indentation unit from every line start inside the visited nodes.</summary>
    private sealed class Dedenter : CSharpSyntaxRewriter
    {
        private readonly SourceText _text;
        private readonly string _unit;

        public Dedenter(SourceText text, string unit)
        {
            _text = text;
            _unit = unit;
        }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
        {
            if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia) || _text.Lines.GetLineFromPosition(trivia.SpanStart).Start != trivia.SpanStart)
            {
                return trivia;
            }

            var value = trivia.ToString();
            return value.StartsWith(_unit, StringComparison.Ordinal) ? Whitespace(value.Substring(_unit.Length)) : trivia;
        }
    }
}
