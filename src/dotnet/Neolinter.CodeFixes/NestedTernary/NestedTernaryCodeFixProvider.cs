using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.NestedTernary;

namespace Neolinter.CodeFixes.NestedTernary;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NestedTernaryCodeFixProvider)), Shared]
public sealed class NestedTernaryCodeFixProvider : CodeFixProvider
{
    private const string Title = "Rewrite as 'if' / 'else if' / 'else'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(NestedTernaryAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var conditional = root?.FindNode(context.Span, getInnermostNodeForTie: true).FirstAncestorOrSelf<ConditionalExpressionSyntax>();
        if (root is null || model is null || conditional is null)
        {
            return;
        }

        // Most nested conditionals have no safe rewrite: the action is offered only when there is one.
        var options = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(conditional.SyntaxTree);
        var preferBraces = options.TryGetValue("csharp_prefer_braces", out var value) ? value.Split(':')[0].Trim().ToLowerInvariant() : null;
        var newRoot = NestedTernaryRewriter.TryRewrite(
            root, model, conditional,
            preferBraces switch { "true" => true, "false" => false, _ => null },
            preferBraces == "when_multiline",
            context.CancellationToken);
        if (newRoot is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(document.WithSyntaxRoot(newRoot)), equivalenceKey: NestedTernaryAnalyzer.DiagnosticId),
            context.Diagnostics);
    }
}
