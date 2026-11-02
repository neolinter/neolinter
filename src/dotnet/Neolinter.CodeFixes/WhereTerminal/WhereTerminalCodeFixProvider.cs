using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.WhereTerminal;

namespace Neolinter.CodeFixes.WhereTerminal;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(WhereTerminalCodeFixProvider)), Shared]
public sealed class WhereTerminalCodeFixProvider : CodeFixProvider
{
    private const string Title = "Pass the predicate to the operator";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(WhereTerminalAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        // The diagnostic sits on the name of 'Where': name, member access, Where call, member access, terminal call.
        var where = root?.FindNode(context.Span).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (root is null || where?.Parent?.Parent is not InvocationExpressionSyntax terminal)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, terminal, ct), equivalenceKey: WhereTerminalAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, InvocationExpressionSyntax terminal, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || WhereTerminalAnalysis.Analyze(terminal, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        // 'x.Where(p).Any()' keeps the receiver and the predicate, and takes the terminal name and the trailing trivia.
        var whereAccess = (MemberAccessExpressionSyntax)candidate.Where.Expression;
        var terminalName = ((MemberAccessExpressionSyntax)terminal.Expression).Name;
        var arguments = candidate.Where.ArgumentList;
        var folded = candidate.Where
            .WithExpression(whereAccess.WithName(terminalName.WithTriviaFrom(whereAccess.Name)))
            .WithArgumentList(arguments.WithCloseParenToken(arguments.CloseParenToken.WithTrailingTrivia(terminal.GetTrailingTrivia())));

        return document.WithSyntaxRoot(root.ReplaceNode(terminal, folded));
    }
}
