using System.Collections.Immutable;
using System.Composition;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.SwitchExpression;

namespace Neolinter.CodeFixes.SwitchExpression;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SwitchExpressionCodeFixProvider)), Shared]
public sealed class SwitchExpressionCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use a switch expression";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(SwitchExpressionAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindToken(context.Span.Start).Parent?.AncestorsAndSelf().OfType<SwitchStatementSyntax>()
            .FirstOrDefault(s => s.SwitchKeyword.Span == context.Span);
        if (root is null || node is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, ct => ReplaceAsync(context.Document, root, node, ct), equivalenceKey: SwitchExpressionAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static async Task<Document> ReplaceAsync(Document document, SyntaxNode root, SwitchStatementSyntax node, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null || SwitchExpressionAnalysis.Analyze(node, model, cancellationToken) is not { } candidate)
        {
            return document;
        }

        // Layout of the statement: its own indentation, one more level for the arms, and the line ends of the file.
        var eol = root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
        var newLine = eol.IsKind(SyntaxKind.EndOfLineTrivia) ? eol.ToString() : Environment.NewLine;
        var indent = node.GetLeadingTrivia().LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).ToString();
        var unit = IndentationUnit(node, indent);

        var text = new StringBuilder();
        text.Append(candidate.Target is null ? "return " : $"{candidate.Target} = ")
            .Append(candidate.Governing).Append(" switch").Append(newLine)
            .Append(indent).Append('{').Append(newLine);
        foreach (var arm in candidate.Arms)
        {
            text.Append(indent).Append(unit).Append(arm.Pattern);
            if (arm.When is not null)
            {
                text.Append(" when ").Append(arm.When);
            }

            text.Append(" => ").Append(arm.Value).Append(',').Append(newLine);
        }

        text.Append(indent).Append("};");

        var statement = SyntaxFactory.ParseStatement(text.ToString())
            .WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(node.GetTrailingTrivia());
        return document.WithSyntaxRoot(root.ReplaceNode(node, statement));
    }

    // The offset of the first label from the 'switch' keyword, when it is plain whitespace; four spaces otherwise.
    private static string IndentationUnit(SwitchStatementSyntax node, string indent)
    {
        var label = node.Sections[0].GetLeadingTrivia().LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).ToString();
        return label.Length > indent.Length && label.StartsWith(indent, StringComparison.Ordinal) ? label.Substring(indent.Length) : "    ";
    }
}
