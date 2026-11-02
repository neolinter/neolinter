using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.CountToAny;

namespace Neolinter.CodeFixes.CountToAny;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CountToAnyCodeFixProvider)), Shared]
public sealed class CountToAnyCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use 'Any()'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(CountToAnyAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true) is not BinaryExpressionSyntax binary)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, binary, ct), equivalenceKey: CountToAnyAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, BinaryExpressionSyntax binary, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || CountToAnyAnalysis.Analyze(binary, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        var count = candidate.Count;
        var access = (MemberAccessExpressionSyntax)count.Expression;
        var any = count
            .WithExpression(access.WithName(access.Name.WithIdentifier(SyntaxFactory.Identifier(default, "Any", default))))
            .WithoutTrivia();
        ExpressionSyntax value = candidate.Negate ? LogicalNot.Of(any) : any;

        return document.WithSyntaxRoot(root.ReplaceNode(binary, value.WithTriviaFrom(binary)));
    }
}
