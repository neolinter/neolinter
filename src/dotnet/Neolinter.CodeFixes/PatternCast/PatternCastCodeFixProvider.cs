using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.PatternCast;

namespace Neolinter.CodeFixes.PatternCast;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PatternCastCodeFixProvider)), Shared]
public sealed class PatternCastCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use a type pattern";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(PatternCastAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<BinaryExpressionSyntax>()
            .FirstOrDefault(b => b.Span == context.Span);
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, node, ct), equivalenceKey: PatternCastAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, BinaryExpressionSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || PatternCastAnalysis.Analyze(node, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        var space = SyntaxFactory.TriviaList(SyntaxFactory.Space);
        var right = (TypeSyntax)node.Right;
        if (candidate.Form == PatternCastForm.IsThenCast)
        {
            var name = PatternCastAnalysis.FreeName(candidate, model, cancellationToken);
            var designation = SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(name).WithTrailingTrivia(right.GetTrailingTrivia()));
            var pattern = SyntaxFactory.DeclarationPattern(right.WithTrailingTrivia(space), designation);
            var isPattern = SyntaxFactory.IsPatternExpression(node.Left, node.OperatorToken, pattern);
            var changed = root.ReplaceNodes(
                candidate.Casts.Cast<SyntaxNode>().Prepend(node),
                (original, _) => original == node ? isPattern : SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(name)).WithTriviaFrom(original));
            return document.WithSyntaxRoot(changed);
        }

        // 'var x = e as T; if (x != null)' and 'var x = e as T; if (x == null) return;': the declaration goes, the 'if' takes its place.
        var declaration = candidate.Declaration!;
        var typePattern = SyntaxFactory.DeclarationPattern(
            right.WithoutTrivia().WithTrailingTrivia(space),
            SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(candidate.Name!)));
        var isToken = SyntaxFactory.Token(space, SyntaxKind.IsKeyword, space);
        PatternSyntax patternSyntax = candidate.Form == PatternCastForm.AsThenGuard
            ? SyntaxFactory.UnaryPattern(SyntaxFactory.Token(SyntaxKind.NotKeyword).WithTrailingTrivia(space), typePattern)
            : typePattern;
        var check = SyntaxFactory.IsPatternExpression(node.Left.WithoutTrivia(), isToken, patternSyntax).WithTriviaFrom(candidate.Check!);

        var ifStatement = candidate.If;
        var newIf = ifStatement
            .WithCondition(ifStatement.Condition.ReplaceNode(candidate.Check!, check))
            .WithLeadingTrivia(declaration.GetLeadingTrivia());
        var block = (BlockSyntax)declaration.Parent!;
        var index = block.Statements.IndexOf(declaration);
        var newBlock = block.WithStatements(block.Statements.RemoveAt(index + 1).RemoveAt(index).Insert(index, newIf));
        return document.WithSyntaxRoot(root.ReplaceNode(block, newBlock));
    }
}
