using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.ExplicitBoolean;
using Neolinter.Analyzers.NestedTernary;

namespace Neolinter.Analyzers.IfElseTernary;

/// <summary>An <c>if</c> that only assigns or returns, with the statement that replaces it.</summary>
internal sealed class IfElseTernaryCandidate(IfStatementSyntax statement, ReturnStatementSyntax? following, string replacement)
{
    public IfStatementSyntax Statement { get; } = statement;

    /// <summary>For <c>if (c) return a; return b;</c>: the final <c>return</c>, which the fix removes.</summary>
    public ReturnStatementSyntax? Following { get; } = following;

    /// <summary>The new statement, without trivia.</summary>
    public string Replacement { get; } = replacement;
}

/// <summary>
/// Shared by the NLR0020 analyzer and code fix. <c>if (c) x = a; else x = b;</c> becomes <c>x = c ? a : b;</c>, and
/// <c>if (c) return a; else return b;</c> (or <c>return b;</c> right after the <c>if</c>) becomes <c>return c ? a : b;</c>.
/// Not reported: what NLR0010 (<c>true</c> and <c>false</c>) and NLR0007 (nested conditionals) own, <c>else if</c> chains,
/// comments, long or multi-line parts, expression variables of an assignment (they would leak into the enclosing block),
/// and a typing that differs between the statements and the conditional expression.
/// </summary>
internal static class IfElseTernaryAnalysis
{
    /// <summary>Longest branch expression, in characters; above it the conditional expression reads worse than the <c>if</c>.</summary>
    internal const int MaxBranchLength = 60;

    public static IfElseTernaryCandidate? Analyze(IfStatementSyntax ifStatement, SemanticModel model, CancellationToken cancellationToken)
    {
        if (ifStatement.Parent is ElseClauseSyntax
            || ifStatement.ContainsDirectives
            || ifStatement.ContainsDiagnostics
            || ifStatement.Condition is AssignmentExpressionSyntax or AnonymousFunctionExpressionSyntax
            || !TryGetBranch(ifStatement.Statement, out var whenTrue))
        {
            return null;
        }

        ReturnStatementSyntax? following = null;
        StatementSyntax other;
        if (ifStatement.Else is { } elseClause)
        {
            other = elseClause.Statement;
        }
        else if (ExplicitBooleanAnalysis.NextStatement(ifStatement) is ReturnStatementSyntax next && !next.ContainsDirectives)
        {
            following = next;
            other = next;
        }
        else
        {
            return null;
        }

        if (!TryGetBranch(other, out var whenFalse))
        {
            return null;
        }

        // 'return a' on both sides, or 'x = a' with the same simple target on both.
        ExpressionSyntax? target = null;
        ExpressionSyntax a, b;
        if (whenTrue is ReturnStatementSyntax { Expression: { } returnedA } && whenFalse is ReturnStatementSyntax { Expression: { } returnedB })
        {
            (a, b) = (returnedA, returnedB);
        }
        else if (whenTrue is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax first }
            && whenFalse is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax second }
            && first.IsKind(SyntaxKind.SimpleAssignmentExpression) && second.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && IsSimpleTarget(first.Left) && SyntaxFactory.AreEquivalent(first.Left, second.Left))
        {
            target = first.Left;
            (a, b) = (first.Right, second.Right);
        }
        else
        {
            return null;
        }

        var tokens = following is null ? ifStatement.DescendantTokens() : ifStatement.DescendantTokens().Concat(following.DescendantTokens());
        if (!IsPlain(a) || !IsPlain(b)
            || (IsBooleanLiteral(a) && IsBooleanLiteral(b))
            || a.ToString().Length > MaxBranchLength || b.ToString().Length > MaxBranchLength
            || IsMultiLine(a) || IsMultiLine(b) || IsMultiLine(ifStatement.Condition)
            || ExplicitBooleanAnalysis.HasLostComment(tokens)
            || (target is not null && DeclaresVariable(ifStatement.Condition)))
        {
            return null;
        }

        var conditional = SyntaxFactory.ConditionalExpression(ifStatement.Condition.WithoutTrivia(), a.WithoutTrivia(), b.WithoutTrivia());
        if (NestedTernaryAnalysis.IsReported(conditional))
        {
            return null;
        }

        var text = (target is null ? "return " : target.WithoutTrivia() + " = ")
            + ifStatement.Condition.WithoutTrivia() + " ? " + a.WithoutTrivia() + " : " + b.WithoutTrivia() + ";";
        if (!ParsesAsExpected(text, ifStatement.Condition, out var parsed)
            || !TypingIsUnchanged(ifStatement, a, b, parsed, model, cancellationToken))
        {
            return null;
        }

        return new IfElseTernaryCandidate(ifStatement, following, text);
    }

    // A statement, or a block that holds only that statement: 'return a;' or 'x = a;'.
    private static bool TryGetBranch(StatementSyntax statement, out StatementSyntax branch)
    {
        branch = statement is BlockSyntax { Statements: { Count: 1 } statements } ? statements[0] : statement;
        return branch is ReturnStatementSyntax { Expression: not null and not RefExpressionSyntax } or ExpressionStatementSyntax;
    }

    // These would need parentheses inside the conditional expression; they are rare enough to leave alone.
    private static bool IsPlain(ExpressionSyntax expression) =>
        expression is not (AssignmentExpressionSyntax or AnonymousFunctionExpressionSyntax or RefExpressionSyntax);

    private static bool IsBooleanLiteral(ExpressionSyntax expression) =>
        expression.IsKind(SyntaxKind.TrueLiteralExpression) || expression.IsKind(SyntaxKind.FalseLiteralExpression);

    private static bool IsMultiLine(SyntaxNode node) => node.ToString().Contains('\n');

    // Evaluated once either way: only names, 'this.' members and chains of them, never calls or indexers.
    private static bool IsSimpleTarget(ExpressionSyntax target) => target switch
    {
        IdentifierNameSyntax or ThisExpressionSyntax => true,
        MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression, Name: IdentifierNameSyntax } member => IsSimpleTarget(member.Expression),
        _ => false,
    };

    // An 'out var' or pattern variable of an 'if' stays in the 'if'; in an expression statement it would join the enclosing block.
    private static bool DeclaresVariable(ExpressionSyntax condition) =>
        condition.DescendantNodesAndSelf().Any(n => n is DeclarationExpressionSyntax or DeclarationPatternSyntax or VarPatternSyntax
            || n is RecursivePatternSyntax { Designation: not null });

    // 'o is T ? a : b' and similar can parse differently: the text must read back as the conditional it was built from.
    private static bool ParsesAsExpected(string text, ExpressionSyntax condition, out ConditionalExpressionSyntax parsed)
    {
        parsed = null!;
        var statement = SyntaxFactory.ParseStatement(text);
        var expression = statement switch
        {
            ReturnStatementSyntax { Expression: { } e } => e,
            ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } => assignment.Right,
            _ => null,
        };
        if (statement.ContainsDiagnostics || expression is not ConditionalExpressionSyntax conditional
            || !SyntaxFactory.AreEquivalent(conditional.Condition, condition))
        {
            return false;
        }

        parsed = conditional;
        return true;
    }

    // Each statement converts its value to the target type; the conditional expression must do the same. A natural type
    // (the type both branches convert to) wins over the target type, so it has to be the target type itself. Without one,
    // C# 9 converts each branch to the target type, as the statements did; before C# 9 that is an error.
    private static bool TypingIsUnchanged(IfStatementSyntax ifStatement, ExpressionSyntax a, ExpressionSyntax b, ConditionalExpressionSyntax parsed, SemanticModel model, CancellationToken cancellationToken)
    {
        var target = model.GetTypeInfo(a, cancellationToken).ConvertedType;
        var other = model.GetTypeInfo(b, cancellationToken).ConvertedType;
        if (target is null or { TypeKind: TypeKind.Error or TypeKind.Dynamic } || !SymbolEqualityComparer.Default.Equals(target, other))
        {
            return false;
        }

        var natural = model.GetSpeculativeTypeInfo(ifStatement.SpanStart, parsed, SpeculativeBindingOption.BindAsExpression).Type;
        if (natural is { TypeKind: not TypeKind.Error })
        {
            return SymbolEqualityComparer.Default.Equals(natural, target);
        }

        return ifStatement.SyntaxTree.Options is CSharpParseOptions options && options.LanguageVersion.MapSpecifiedToEffectiveVersion() >= LanguageVersion.CSharp9;
    }
}
