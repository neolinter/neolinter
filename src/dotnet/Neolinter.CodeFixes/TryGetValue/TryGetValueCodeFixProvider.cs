using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.TryGetValue;

namespace Neolinter.CodeFixes.TryGetValue;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TryGetValueCodeFixProvider)), Shared]
public sealed class TryGetValueCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use 'TryGetValue'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(TryGetValueAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var check = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Span == context.Span);
        if (root is null || check is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, check, ct), equivalenceKey: TryGetValueAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, InvocationExpressionSyntax check, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || TryGetValueAnalysis.Analyze(check, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        var name = TryGetValueAnalysis.FreeName(check, model, cancellationToken);
        var access = (MemberAccessExpressionSyntax)check.Expression;
        var space = SyntaxFactory.TriviaList(SyntaxFactory.Space);
        var declaration = SyntaxFactory.DeclarationExpression(
            SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(SyntaxFactory.TriviaList(), "var", space)),
            SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(name)));
        var outArgument = SyntaxFactory.Argument(null, SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.OutKeyword, space), declaration);
        var arguments = SyntaxFactory.SeparatedList<ArgumentSyntax>(new SyntaxNodeOrToken[]
        {
            check.ArgumentList.Arguments[0],
            SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.CommaToken, space),
            outArgument,
        });
        var newCheck = check
            .WithExpression(access.WithName(SyntaxFactory.IdentifierName(SyntaxFactory.Identifier("TryGetValue")).WithTriviaFrom(access.Name)))
            .WithArgumentList(check.ArgumentList.WithArguments(arguments));

        var changed = root.ReplaceNodes(
            candidate.Reads.Cast<SyntaxNode>().Prepend(check),
            (original, _) => original == check ? newCheck : SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(name)).WithTriviaFrom(original));
        return document.WithSyntaxRoot(changed);
    }
}
