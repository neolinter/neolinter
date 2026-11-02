using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.ForeachWhere;
using Neolinter.CodeFixes.InvertIf;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Neolinter.CodeFixes.ForeachWhere;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ForeachWhereCodeFixProvider)), Shared]
public sealed class ForeachWhereCodeFixProvider : CodeFixProvider
{
    private const string Title = "Move the guard into 'Where'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ForeachWhereAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<IfStatementSyntax>()
            .FirstOrDefault(s => s.Span == context.Span);
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, node, breakGuards: false, ct), equivalenceKey: ForeachWhereAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    internal static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, IfStatementSyntax first, bool breakGuards, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || ForeachWhereAnalysis.Analyze(first, breakGuards, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        // Kept items are those the guards do not skip: each condition negated, joined with '&&'.
        ExpressionSyntax? predicate = null;
        foreach (var guard in candidate.Guards)
        {
            var kept = ConditionNegator.Negate(guard.Condition, model, simplifyLogicalOperators: false, cancellationToken).WithoutTrivia();
            predicate = predicate is null ? kept : And(ConditionNegator.ParenthesizeFor(predicate, SyntaxKind.LogicalAndExpression), kept);
        }

        var loop = candidate.Loop;
        var lambda = SimpleLambdaExpression(Parameter(Identifier(TriviaList(), candidate.VariableName, TriviaList())), predicate!)
            .WithArrowToken(Token(TriviaList(Space), SyntaxKind.EqualsGreaterThanToken, TriviaList(Space)));
        var where = InvocationExpression(
            MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                loop.Expression.WithoutTrivia(),
                Token(default, SyntaxKind.DotToken, default),
                IdentifierName(Identifier(TriviaList(), candidate.MethodName, TriviaList()))),
            ArgumentList(
                Token(default, SyntaxKind.OpenParenToken, default),
                SingletonSeparatedList(Argument(lambda)),
                Token(default, SyntaxKind.CloseParenToken, default))).WithTriviaFrom(loop.Expression);

        var body = (BlockSyntax)loop.Statement;
        var rest = body.Statements.Skip(candidate.Guards.Length).ToList();
        rest[0] = rest[0].WithLeadingTrivia(WithoutLeadingBlankLines(rest[0].GetLeadingTrivia()));
        var newLoop = loop.WithExpression(where).WithStatement(body.WithStatements(List(rest)));
        var newRoot = root.ReplaceNode(loop, newLoop);

        if (candidate.NeedsUsing && newRoot is CompilationUnitSyntax unit)
        {
            newRoot = AddUsing(unit);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    // 'a && b && c' stays a left-leaning chain, as the parser reads it.
    private static ExpressionSyntax And(ExpressionSyntax left, ExpressionSyntax right) =>
        right is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalAndExpression } chain
            ? BinaryExpression(SyntaxKind.LogicalAndExpression, And(left, chain.Left), chain.OperatorToken, chain.Right)
            : BinaryExpression(
                SyntaxKind.LogicalAndExpression,
                left,
                Token(TriviaList(Space), SyntaxKind.AmpersandAmpersandToken, TriviaList(Space)),
                ConditionNegator.ParenthesizeFor(right, SyntaxKind.LogicalAndExpression));

    // The blank line that separated the guards from the rest would sit at the top of the body.
    private static SyntaxTriviaList WithoutLeadingBlankLines(SyntaxTriviaList trivia)
    {
        var skip = 0;
        while (skip < trivia.Count)
        {
            if (trivia[skip].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                skip++;
            }
            else if (trivia[skip].IsKind(SyntaxKind.WhitespaceTrivia) && skip + 1 < trivia.Count && trivia[skip + 1].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                skip += 2;
            }
            else
            {
                break;
            }
        }

        return TriviaList(trivia.Skip(skip));
    }

    private static CompilationUnitSyntax AddUsing(CompilationUnitSyntax unit)
    {
        var eol = unit.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
        var lineEnd = eol.IsKind(SyntaxKind.EndOfLineTrivia) ? eol : EndOfLine(Environment.NewLine);
        var directive = UsingDirective(ParseName("System.Linq"))
            .WithUsingKeyword(Token(default, SyntaxKind.UsingKeyword, TriviaList(Space)));

        if (unit.Usings.Count > 0)
        {
            return unit.WithUsings(unit.Usings.Add(directive.WithSemicolonToken(Token(default, SyntaxKind.SemicolonToken, TriviaList(lineEnd)))));
        }

        // No usings yet: it goes first, followed by a blank line, and takes the leading blank lines of the file.
        var firstToken = unit.GetFirstToken();
        var added = directive
            .WithLeadingTrivia(firstToken.LeadingTrivia)
            .WithSemicolonToken(Token(default, SyntaxKind.SemicolonToken, TriviaList(lineEnd, lineEnd)));
        return unit.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(TriviaList())).WithUsings(SingletonList(added));
    }
}
