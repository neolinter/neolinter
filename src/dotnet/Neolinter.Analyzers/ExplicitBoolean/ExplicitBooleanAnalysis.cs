using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.ExplicitBoolean;

/// <summary>A construct whose <c>true</c> and <c>false</c> results only restate a <c>bool</c> condition.</summary>
internal sealed class ExplicitBooleanCandidate(SyntaxNode node, ExpressionSyntax condition, bool negate, ReturnStatementSyntax? following)
{
    /// <summary>The <c>if</c> statement or the conditional expression that is replaced.</summary>
    public SyntaxNode Node { get; } = node;

    public ExpressionSyntax Condition { get; } = condition;

    /// <summary>True when the replacement is <c>!condition</c>.</summary>
    public bool Negate { get; } = negate;

    /// <summary>For <c>if (c) return true; return false;</c>: the final <c>return</c>, which the fix removes.</summary>
    public ReturnStatementSyntax? Following { get; } = following;
}

/// <summary>
/// Finds <c>c ? true : false</c>, <c>if (c) return true; else return false;</c> and
/// <c>if (c) return true; return false;</c> (and the reversed forms) where <c>c</c> is a plain <c>bool</c>.
/// Shared by the analyzer, the code fix and NLR0009.
/// </summary>
internal static class ExplicitBooleanAnalysis
{
    public static ExplicitBooleanCandidate? Analyze(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken) =>
        node switch
        {
            ConditionalExpressionSyntax conditional => AnalyzeConditional(conditional, model, cancellationToken),
            IfStatementSyntax ifStatement => AnalyzeIf(ifStatement, model, cancellationToken),
            _ => null,
        };

    private static ExplicitBooleanCandidate? AnalyzeConditional(ConditionalExpressionSyntax conditional, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!IsBooleanLiteral(conditional.WhenTrue, out var whenTrue)
            || !IsBooleanLiteral(conditional.WhenFalse, out var whenFalse)
            || whenTrue == whenFalse
            || HasLostComment(conditional.DescendantTokens())
            || !IsBool(conditional.Condition, model, cancellationToken))
        {
            return null;
        }

        return new ExplicitBooleanCandidate(conditional, conditional.Condition, negate: !whenTrue, following: null);
    }

    private static ExplicitBooleanCandidate? AnalyzeIf(IfStatementSyntax ifStatement, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!TryGetReturnedLiteral(ifStatement.Statement, out var returned))
        {
            return null;
        }

        ReturnStatementSyntax? following = null;
        bool other;
        if (ifStatement.Else is { } elseClause)
        {
            if (!TryGetReturnedLiteral(elseClause.Statement, out other))
            {
                return null;
            }
        }
        else
        {
            // 'if (c) return true;' followed by 'return false;' in the same block or switch section.
            if (NextStatement(ifStatement) is not ReturnStatementSyntax next || !TryGetReturnedLiteral(next, out other))
            {
                return null;
            }

            following = next;
        }

        if (returned == other)
        {
            return null;
        }

        var tokens = following is null
            ? ifStatement.DescendantTokens()
            : ifStatement.DescendantTokens().Concat(following.DescendantTokens());
        if (HasLostComment(tokens) || !IsBool(ifStatement.Condition, model, cancellationToken))
        {
            return null;
        }

        return new ExplicitBooleanCandidate(ifStatement, ifStatement.Condition, negate: !returned, following);
    }

    internal static StatementSyntax? NextStatement(IfStatementSyntax ifStatement)
    {
        var siblings = ifStatement.Parent switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => default,
        };

        var index = siblings.IndexOf(ifStatement);
        return index >= 0 && index + 1 < siblings.Count ? siblings[index + 1] : null;
    }

    // 'return true;' or a block that holds only that statement.
    private static bool TryGetReturnedLiteral(StatementSyntax statement, out bool value)
    {
        if (statement is BlockSyntax { Statements: { Count: 1 } statements })
        {
            statement = statements[0];
        }

        value = false;
        return statement is ReturnStatementSyntax { Expression: { } expression } && IsBooleanLiteral(expression, out value);
    }

    private static bool IsBooleanLiteral(ExpressionSyntax expression, out bool value)
    {
        value = expression.IsKind(SyntaxKind.TrueLiteralExpression);
        return value || expression.IsKind(SyntaxKind.FalseLiteralExpression);
    }

    // Not 'bool?', 'dynamic' or a type with its own 'true' and 'false' operators: the result would not be the same value.
    private static bool IsBool(ExpressionSyntax condition, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetTypeInfo(condition, cancellationToken).Type?.SpecialType == SpecialType.System_Boolean;

    // The rewrite drops the keywords and literals, so a comment or directive between the first and last token would be
    // lost. Trivia before the first token and after the last one stays with the new code.
    internal static bool HasLostComment(IEnumerable<SyntaxToken> tokens)
    {
        var list = tokens.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if ((i > 0 && IsLost(list[i].LeadingTrivia)) || (i < list.Count - 1 && IsLost(list[i].TrailingTrivia)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLost(SyntaxTriviaList trivia) =>
        trivia.Any(t => t.IsDirective || t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
}
