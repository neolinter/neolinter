using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.InvertIf;

internal enum InvertIfKind
{
    /// <summary><c>if</c> without <c>else</c>, last statement of a method or loop body.</summary>
    IfAtBlockEnd,

    /// <summary><c>if</c> without <c>else</c>, followed only by a simple <c>return</c>.</summary>
    IfBeforeReturn,

    /// <summary><c>if</c>/<c>else</c> whose <c>else</c> only leaves the block.</summary>
    ElseExit,

    /// <summary><c>if</c>/<c>else if</c> chain whose final <c>else</c> only leaves the block.</summary>
    ElseIfCascade,
}

internal sealed class InvertIfCandidate
{
    public InvertIfCandidate(IfStatementSyntax ifStatement, BlockSyntax block, InvertIfKind kind, StatementSyntax exit)
    {
        IfStatement = ifStatement;
        Block = block;
        Kind = kind;
        Exit = exit;
    }

    public IfStatementSyntax IfStatement { get; }

    /// <summary>The block that directly contains <see cref="IfStatement"/>.</summary>
    public BlockSyntax Block { get; }

    public InvertIfKind Kind { get; }

    /// <summary>The statement that leaves the block once the condition is inverted.</summary>
    public StatementSyntax Exit { get; }
}

/// <summary>Detection shared by the NLR0001 analyzer and its code fix.</summary>
internal static class InvertIfAnalysis
{
    public const int DefaultMinStatements = 3;

    public static InvertIfCandidate? Analyze(IfStatementSyntax ifStatement, SemanticModel model, int minStatements, CancellationToken cancellationToken)
    {
        // Parent is an ElseClause for "else if": only the head of a cascade is analyzed.
        if (ifStatement.Parent is not BlockSyntax block
            || ifStatement.ContainsDirectives
            || ifStatement.ContainsDiagnostics)
        {
            return null;
        }

        if (ifStatement.Else?.Statement is IfStatementSyntax)
        {
            return AnalyzeCascade(ifStatement, block, model, cancellationToken);
        }

        var body = GetStatements(ifStatement.Statement);
        if (body.Count == 0)
        {
            return null;
        }

        var index = block.Statements.IndexOf(ifStatement);
        var followingCount = block.Statements.Count - index - 1;

        if (ifStatement.Else is { } elseClause)
        {
            var exit = SingleStatement(elseClause.Statement);
            if (exit is null || !IsExit(exit) || (body.Count == 1 && IsExit(body[0])))
            {
                return null;
            }

            return CanHoist(ifStatement, block, body, followingCount > 0)
                ? new InvertIfCandidate(ifStatement, block, InvertIfKind.ElseExit, elseClause.Statement)
                : null;
        }

        if (body.Count < minStatements)
        {
            return null;
        }

        if (followingCount == 0)
        {
            var exit = ExitAtBlockEnd(block, model, cancellationToken);
            return exit is not null && CanHoist(ifStatement, block, body, statementsFollow: false)
                ? new InvertIfCandidate(ifStatement, block, InvertIfKind.IfAtBlockEnd, exit)
                : null;
        }

        // Non-void methods: the trailing return is copied into the inverted if. Each path still evaluates it
        // once, so behaviour is unchanged; simple expressions only keep the duplicated code trivial.
        if (followingCount == 1
            && block.Statements[index + 1] is ReturnStatementSyntax returnStatement
            && IsFunctionBody(block)
            && (returnStatement.Expression is null || IsSimple(returnStatement.Expression))
            && CanHoist(ifStatement, block, body, statementsFollow: true))
        {
            return new InvertIfCandidate(ifStatement, block, InvertIfKind.IfBeforeReturn, returnStatement);
        }

        return null;
    }

    /// <summary>
    /// <c>if (c1) B1 else if (c2) B2 else EXIT</c> becomes <c>if (!c1 &amp;&amp; !c2) EXIT</c> followed by
    /// <c>if (c1) B1 else B2</c>. The conditions are evaluated twice, so every one must be free of side effects.
    /// </summary>
    private static InvertIfCandidate? AnalyzeCascade(IfStatementSyntax head, BlockSyntax block, SemanticModel model, CancellationToken cancellationToken)
    {
        var current = head;
        while (true)
        {
            var branch = SingleStatement(current.Statement);
            if ((branch is not null && IsExit(branch)) || !IsPure(current.Condition, model, cancellationToken))
            {
                return null;
            }

            if (current.Else is not { } elseClause)
            {
                return null;
            }

            if (HasComment(elseClause.ElseKeyword.LeadingTrivia) || HasComment(elseClause.ElseKeyword.TrailingTrivia))
            {
                return null;
            }

            // The trailing trivia of the last branch is replaced by the end of the whole statement.
            if (HasComment(current.Statement.GetLastToken().TrailingTrivia))
            {
                return null;
            }

            if (elseClause.Statement is IfStatementSyntax next)
            {
                // Comments here would be lost: the condition moves into the guard and its tokens disappear.
                if (HasComment(next.IfKeyword.LeadingTrivia) || HasComment(next.IfKeyword.TrailingTrivia)
                    || HasComment(next.OpenParenToken.TrailingTrivia) || HasComment(next.CloseParenToken.LeadingTrivia)
                    || next.Condition.DescendantTrivia().Any(t => IsComment(t)))
                {
                    return null;
                }

                current = next;
                continue;
            }

            var exit = SingleStatement(elseClause.Statement);
            return exit is not null && IsExit(exit)
                ? new InvertIfCandidate(head, block, InvertIfKind.ElseIfCascade, elseClause.Statement)
                : null;
        }
    }

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);

    private static bool HasComment(SyntaxTriviaList trivia) => trivia.Any(IsComment);

    /// <summary>
    /// Expressions that can be evaluated twice without changing behaviour: names, literals, member reads
    /// and built-in comparison and logical operators. Calls and user-defined operators are rejected.
    /// </summary>
    private static bool IsPure(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax or IdentifierNameSyntax or ThisExpressionSyntax or DefaultExpressionSyntax:
                return true;
            case ParenthesizedExpressionSyntax parenthesized:
                return IsPure(parenthesized.Expression, model, cancellationToken);
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression) && IsPure(memberAccess.Expression, model, cancellationToken);
            case PrefixUnaryExpressionSyntax prefix:
                return prefix.Kind() is SyntaxKind.LogicalNotExpression or SyntaxKind.UnaryMinusExpression
                    && IsPure(prefix.Operand, model, cancellationToken)
                    && IsBuiltIn(prefix, model, cancellationToken);
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.IsExpression):
                return IsPure(binary.Left, model, cancellationToken);
            case BinaryExpressionSyntax binary:
                return binary.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression
                        or SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
                        or SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression
                        or SyntaxKind.GreaterThanExpression or SyntaxKind.GreaterThanOrEqualExpression
                    && IsPure(binary.Left, model, cancellationToken)
                    && IsPure(binary.Right, model, cancellationToken)
                    && IsBuiltIn(binary, model, cancellationToken);
            case IsPatternExpressionSyntax isPattern:
                return IsPure(isPattern.Expression, model, cancellationToken) && IsPurePattern(isPattern.Pattern, model, cancellationToken);
            default:
                return false;
        }
    }

    // Patterns that declare no variable and evaluate no call.
    private static bool IsPurePattern(PatternSyntax pattern, SemanticModel model, CancellationToken cancellationToken) => pattern switch
    {
        ConstantPatternSyntax constant => IsPure(constant.Expression, model, cancellationToken),
        RelationalPatternSyntax relational => IsPure(relational.Expression, model, cancellationToken),
        TypePatternSyntax => true,
        UnaryPatternSyntax unary => IsPurePattern(unary.Pattern, model, cancellationToken),
        BinaryPatternSyntax binary => IsPurePattern(binary.Left, model, cancellationToken) && IsPurePattern(binary.Right, model, cancellationToken),
        ParenthesizedPatternSyntax parenthesized => IsPurePattern(parenthesized.Pattern, model, cancellationToken),
        _ => false,
    };

    // No symbol means a built-in operator (Roslyn reports none for && and ||). Strings and decimals report their
    // operators as user-defined methods but they have no side effects.
    private static bool IsBuiltIn(ExpressionSyntax operation, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetSymbolInfo(operation, cancellationToken).Symbol is not IMethodSymbol method
        || method.MethodKind == MethodKind.BuiltinOperator
        || method.ContainingType.SpecialType is SpecialType.System_String or SpecialType.System_Decimal;

    public static IReadOnlyList<StatementSyntax> GetStatements(StatementSyntax statement) =>
        statement is BlockSyntax block ? block.Statements : new[] { statement };

    private static StatementSyntax? SingleStatement(StatementSyntax statement) =>
        statement is BlockSyntax block
            ? block.Statements.Count == 1 ? block.Statements[0] : null
            : statement;

    private static bool IsExit(StatementSyntax statement) =>
        statement is ReturnStatementSyntax or ContinueStatementSyntax or BreakStatementSyntax or ThrowStatementSyntax
        || statement.IsKind(SyntaxKind.YieldBreakStatement);

    private static bool IsSimple(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax or DefaultExpressionSyntax or IdentifierNameSyntax or ThisExpressionSyntax => true,
        MemberAccessExpressionSyntax memberAccess => memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression) && IsSimple(memberAccess.Expression),
        _ => false,
    };

    private static StatementSyntax? ExitAtBlockEnd(BlockSyntax block, SemanticModel model, CancellationToken cancellationToken)
    {
        // Parsed rather than built with SyntaxFactory helpers: no elastic trivia, so hosts do not reformat around it.
        if (block.Parent is CommonForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
        {
            return SyntaxFactory.ParseStatement("continue;");
        }

        if (!IsFunctionBody(block))
        {
            return null;
        }

        if (IsIterator(block))
        {
            return SyntaxFactory.ParseStatement("yield break;");
        }

        return ReturnsNothing(block, model, cancellationToken) ? SyntaxFactory.ParseStatement("return;") : null;
    }

    // The search for an exit stops at the nearest function: lambdas and local functions are boundaries.
    private static bool IsFunctionBody(BlockSyntax block) =>
        block.Parent is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;

    private static bool ReturnsNothing(BlockSyntax body, SemanticModel model, CancellationToken cancellationToken)
    {
        var symbol = body.Parent is AnonymousFunctionExpressionSyntax function
            ? model.GetSymbolInfo(function, cancellationToken).Symbol
            : model.GetDeclaredSymbol(body.Parent!, cancellationToken);

        if (symbol is not IMethodSymbol method || IsIterator(body))
        {
            return false;
        }

        return method.ReturnsVoid
            || (method.IsAsync
                && method.ReturnType is INamedTypeSymbol { Arity: 0, Name: "Task" or "ValueTask" } returnType
                && returnType.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks");
    }

    private static bool IsIterator(BlockSyntax body) =>
        body.DescendantNodes(node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<YieldStatementSyntax>()
            .Any();

    /// <summary>
    /// Moving the body one level up must not change meaning: no name clash with the
    /// enclosing block, and no <c>using</c> declaration whose disposal would be delayed.
    /// </summary>
    private static bool CanHoist(IfStatementSyntax ifStatement, BlockSyntax block, IReadOnlyList<StatementSyntax> body, bool statementsFollow)
    {
        if (statementsFollow && body.Any(s => s is LocalDeclarationStatementSyntax declaration && declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)))
        {
            return false;
        }

        var hoisted = new HashSet<string>(body.SelectMany(DeclaredNames));
        if (hoisted.Count == 0)
        {
            return true;
        }

        return !block.DescendantNodes(node => node != ifStatement)
            .Where(node => node != ifStatement)
            .Select(DeclaredIdentifier)
            .Any(identifier => identifier is { } token && hoisted.Contains(token.ValueText));
    }

    /// <summary>Names a top-level statement of the body introduces into the body's own scope.</summary>
    private static IEnumerable<string> DeclaredNames(StatementSyntax statement)
    {
        switch (statement)
        {
            case LocalDeclarationStatementSyntax declaration:
                foreach (var variable in declaration.Declaration.Variables)
                {
                    yield return variable.Identifier.ValueText;
                }

                break;
            case LocalFunctionStatementSyntax localFunction:
                yield return localFunction.Identifier.ValueText;
                yield break;
            case LabeledStatementSyntax labeled:
                yield return labeled.Identifier.ValueText;
                foreach (var name in DeclaredNames(labeled.Statement))
                {
                    yield return name;
                }

                yield break;
        }

        // Expression variables (out var, patterns) leak into the enclosing block for these statements.
        SyntaxNode? scope = statement switch
        {
            LocalDeclarationStatementSyntax or ExpressionStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax => statement,
            IfStatementSyntax nestedIf => nestedIf.Condition,
            SwitchStatementSyntax switchStatement => switchStatement.Expression,
            _ => null,
        };

        if (scope is null)
        {
            yield break;
        }

        foreach (var designation in scope.DescendantNodesAndSelf(node => node is not AnonymousFunctionExpressionSyntax).OfType<SingleVariableDesignationSyntax>())
        {
            yield return designation.Identifier.ValueText;
        }
    }

    private static SyntaxToken? DeclaredIdentifier(SyntaxNode node) => node switch
    {
        VariableDeclaratorSyntax n => n.Identifier,
        SingleVariableDesignationSyntax n => n.Identifier,
        ForEachStatementSyntax n => n.Identifier,
        CatchDeclarationSyntax n => n.Identifier,
        LocalFunctionStatementSyntax n => n.Identifier,
        ParameterSyntax n => n.Identifier,
        LabeledStatementSyntax n => n.Identifier,
        FromClauseSyntax n => n.Identifier,
        LetClauseSyntax n => n.Identifier,
        JoinClauseSyntax n => n.Identifier,
        _ => null,
    };
}
