using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.IgnoredReturnValue;

namespace Neolinter.CodeFixes.IgnoredReturnValue;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IgnoredReturnValueCodeFixProvider)), Shared]
public sealed class IgnoredReturnValueCodeFixProvider : CodeFixProvider
{
    private const string Title = "Assign the result back to the receiver";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(IgnoredReturnValueAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var statement = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<ExpressionStatementSyntax>().FirstOrDefault();
        if (root is null || model is null || statement is null
            || IgnoredReturnValueAnalysis.GetAssignableReceiver(statement, model, context.CancellationToken) is not { } receiver)
        {
            return;
        }

        var invocation = (InvocationExpressionSyntax)statement.Expression;
        var assignment = SyntaxFactory.AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            receiver.WithoutTrivia().WithLeadingTrivia(invocation.GetLeadingTrivia()).WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.Token(default, SyntaxKind.EqualsToken, SyntaxFactory.TriviaList(SyntaxFactory.Space)),
            invocation.WithLeadingTrivia(default(SyntaxTriviaList)));

        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(statement, statement.WithExpression(assignment)))), equivalenceKey: IgnoredReturnValueAnalyzer.DiagnosticId),
            context.Diagnostics);
    }
}
