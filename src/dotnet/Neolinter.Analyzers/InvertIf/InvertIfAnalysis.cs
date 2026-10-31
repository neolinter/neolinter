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

/// <summary>Detection shared by the NL0001 analyzer and its code fix.</summary>
internal static class InvertIfAnalysis
{
    public const int DefaultMinStatements = 3;

    public static InvertIfCandidate? Analyze(IfStatementSyntax ifStatement, SemanticModel model, int minStatements, CancellationToken cancellationToken)
    {
        // Parent is an ElseClause for "else if": cascades are out of scope for v1.
        if (ifStatement.Parent is not BlockSyntax block
            || ifStatement.Else?.Statement is IfStatementSyntax
            || ifStatement.ContainsDirectives
            || ifStatement.ContainsDiagnostics)
        {
            return null;
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

    public static IReadOnlyList<StatementSyntax> GetStatements(StatementSyntax statement) =>
        statement is BlockSyntax block ? block.Statements : new[] { statement };

    private static StatementSyntax? SingleStatement(StatementSyntax statement) =>
        statement is BlockSyntax block
            ? block.Statements.Count == 1 ? block.Statements[0] : null
            : statement;

    private static bool IsExit(StatementSyntax statement) =>
        statement is ReturnStatementSyntax or ContinueStatementSyntax or BreakStatementSyntax or ThrowStatementSyntax;

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

        return IsFunctionBody(block) && ReturnsNothing(block, model, cancellationToken)
            ? SyntaxFactory.ParseStatement("return;")
            : null;
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
