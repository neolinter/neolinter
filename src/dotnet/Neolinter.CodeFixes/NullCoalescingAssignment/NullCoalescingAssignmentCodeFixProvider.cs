using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.NullCoalescingAssignment;

namespace Neolinter.CodeFixes.NullCoalescingAssignment;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NullCoalescingAssignmentCodeFixProvider)), Shared]
public sealed class NullCoalescingAssignmentCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use '??='";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(NullCoalescingAssignmentAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindToken(context.Span.Start).Parent;

        // The diagnostic sits on the 'if' keyword or on the '=' of 'x = x ?? v'.
        var statement = node as StatementSyntax ?? (node as AssignmentExpressionSyntax)?.Parent as StatementSyntax;
        if (root is null || statement is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, statement, ct), equivalenceKey: NullCoalescingAssignmentAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, StatementSyntax statement, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || NullCoalescingAssignmentAnalysis.Analyze(statement, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        // Tokens carry explicit trivia (no elastic trivia), so the host keeps the user's layout.
        var space = SyntaxFactory.TriviaList(SyntaxFactory.Space);
        var assignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.CoalesceAssignmentExpression,
            candidate.Target.WithoutTrivia(),
            SyntaxFactory.Token(space, SyntaxKind.QuestionQuestionEqualsToken, space),
            candidate.Value.WithoutTrivia());
        var replacement = SyntaxFactory.ExpressionStatement(assignment, SyntaxFactory.Token(default, SyntaxKind.SemicolonToken, default))
            .WithTriviaFrom(candidate.Statement);

        return document.WithSyntaxRoot(root.ReplaceNode(candidate.Statement, replacement));
    }
}
