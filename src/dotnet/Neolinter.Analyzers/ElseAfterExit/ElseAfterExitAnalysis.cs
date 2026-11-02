using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.ExplicitBoolean;
using Neolinter.Analyzers.InvertIf;

namespace Neolinter.Analyzers.ElseAfterExit;

/// <summary>Detection shared by the NLR0009 analyzer and its code fix.</summary>
internal static class ElseAfterExitAnalysis
{
    /// <summary>
    /// The block that directly contains <paramref name="ifStatement"/> when its <c>else</c> can be removed by moving
    /// the statements of the <c>else</c> after the <c>if</c>; null when the rule does not apply.
    /// </summary>
    public static BlockSyntax? Analyze(IfStatementSyntax ifStatement, SemanticModel model, CancellationToken cancellationToken)
    {
        // A parent that is not a block rules out 'else if', a body without braces (dangling else) and switch sections,
        // where the statements would join the scope and the fall-through analysis of the whole switch.
        if (ifStatement.Parent is not BlockSyntax block
            || ifStatement.Else is not { Statement: not IfStatementSyntax } elseClause
            || ifStatement.ContainsDirectives
            || ifStatement.ContainsDiagnostics
            || !EndsInExit(ifStatement.Statement)
            || !StartsLine(ifStatement.GetFirstToken()))
        {
            return null;
        }

        var moved = InvertIfAnalysis.GetStatements(elseClause.Statement);
        if (moved.Count == 0 || !HasSafeLayout(ifStatement, elseClause, moved))
        {
            return null;
        }

        // NLR0001 already reports an 'else' that is a lone exit after a longer branch, and its fix removes it.
        if (InvertIfAnalysis.Analyze(ifStatement, model, InvertIfAnalysis.DefaultMinStatements, cancellationToken) is { Kind: InvertIfKind.ElseExit })
        {
            return null;
        }

        // NLR0010 reports 'if (c) return true; else return false;' and its fix writes 'return c;', which leaves no 'else'.
        if (ExplicitBooleanAnalysis.Analyze(ifStatement, model, cancellationToken) is not null)
        {
            return null;
        }

        return CanMoveUp(ifStatement, elseClause, block, moved) ? block : null;
    }

    private static bool EndsInExit(StatementSyntax statement) => statement switch
    {
        ReturnStatementSyntax or ThrowStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax or GotoStatementSyntax => true,
        BlockSyntax block => block.Statements.Count > 0 && EndsInExit(block.Statements.Last()),
        IfStatementSyntax { Else: { } other } nested => EndsInExit(nested.Statement) && EndsInExit(other.Statement),
        _ => statement.IsKind(SyntaxKind.YieldBreakStatement),
    };

    public static bool StartsLine(SyntaxToken token) => HasEndOfLine(token.GetPreviousToken().TrailingTrivia);

    public static bool HasEndOfLine(SyntaxTriviaList trivia) => trivia.Any(SyntaxKind.EndOfLineTrivia);

    private static bool HasComment(SyntaxTriviaList trivia) =>
        trivia.Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia));

    /// <summary>
    /// The fix drops the tokens of the <c>else</c> and its braces: it must not drop a comment with them, and it
    /// moves each statement onto its own line, which needs the later ones to already be there.
    /// </summary>
    private static bool HasSafeLayout(IfStatementSyntax ifStatement, ElseClauseSyntax elseClause, IReadOnlyList<StatementSyntax> moved)
    {
        if (HasComment(elseClause.ElseKeyword.LeadingTrivia) || HasComment(elseClause.ElseKeyword.TrailingTrivia))
        {
            return false;
        }

        // The end of the 'if' gets a line break when it has none; a comment there would be lost.
        var ifEnd = ifStatement.Statement.GetLastToken().TrailingTrivia;
        if (!HasEndOfLine(ifEnd) && HasComment(ifEnd))
        {
            return false;
        }

        var last = moved[moved.Count - 1].GetLastToken().TrailingTrivia;
        if (elseClause.Statement is BlockSyntax block)
        {
            if (HasComment(block.OpenBraceToken.LeadingTrivia) || HasComment(block.OpenBraceToken.TrailingTrivia)
                || HasComment(block.CloseBraceToken.LeadingTrivia) || HasComment(block.CloseBraceToken.TrailingTrivia))
            {
                return false;
            }

            for (var i = 1; i < moved.Count; i++)
            {
                if (!StartsLine(moved[i].GetFirstToken()))
                {
                    return false;
                }
            }
        }

        return HasEndOfLine(last) || !HasComment(last);
    }

    /// <summary>
    /// The statements keep their meaning one level up when no name they declare is used or declared elsewhere in the
    /// block (a clash would not compile, or a later use of a field with that name would bind to the new local),
    /// and no <c>using</c> declaration has its disposal delayed by code that follows.
    /// </summary>
    private static bool CanMoveUp(IfStatementSyntax ifStatement, ElseClauseSyntax elseClause, BlockSyntax block, IReadOnlyList<StatementSyntax> moved)
    {
        var statementsFollow = block.Statements.IndexOf(ifStatement) < block.Statements.Count - 1;
        if (statementsFollow && moved.Any(s => s is LocalDeclarationStatementSyntax declaration && declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)))
        {
            return false;
        }

        var declared = new HashSet<string>(moved.SelectMany(InvertIfAnalysis.DeclaredNames));
        return declared.Count == 0
            || !block.DescendantTokens()
                .Any(token => token.IsKind(SyntaxKind.IdentifierToken)
                    && !elseClause.FullSpan.Contains(token.FullSpan)
                    && declared.Contains(token.ValueText));
    }
}
