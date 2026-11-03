using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.MultipleEnumeration;

namespace Neolinter.CodeFixes.MultipleEnumeration;

/// <summary>Materializes a local sequence once, at its declaration: <c>var q = source.Where(..);</c> becomes <c>var q = source.Where(..).ToList();</c>.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MultipleEnumerationCodeFixProvider)), Shared]
public sealed class MultipleEnumerationCodeFixProvider : CodeFixProvider
{
    private const string Title = "Materialize the sequence with ToList()";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(MultipleEnumerationAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true) is not IdentifierNameSyntax identifier
            || model is null
            || model.GetSymbolInfo(identifier, context.CancellationToken).Symbol is not ILocalSymbol { Type: INamedTypeSymbol { IsGenericType: true } type } local
            || type.OriginalDefinition.SpecialType != SpecialType.System_Collections_Generic_IEnumerable_T
            || local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax { Initializer.Value: { } initializer } declarator
            || declarator.SyntaxTree != root.SyntaxTree)
        {
            return;
        }

        var materialized = ToList(initializer);
        if (model.GetSpeculativeSymbolInfo(initializer.SpanStart, materialized, SpeculativeBindingOption.BindAsExpression).Symbol is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(initializer, materialized))), equivalenceKey: MultipleEnumerationAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static InvocationExpressionSyntax ToList(ExpressionSyntax source)
    {
        var receiver = source.WithoutTrivia();
        if (receiver is not (InvocationExpressionSyntax or MemberAccessExpressionSyntax or IdentifierNameSyntax or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax))
        {
            receiver = SyntaxFactory.ParenthesizedExpression(receiver);
        }

        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, SyntaxFactory.IdentifierName("ToList")))
            .WithLeadingTrivia(source.GetLeadingTrivia())
            .WithTrailingTrivia(source.GetTrailingTrivia());
    }
}
