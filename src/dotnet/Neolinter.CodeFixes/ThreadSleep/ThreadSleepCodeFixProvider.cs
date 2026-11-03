using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.BlockingTask;
using Neolinter.Analyzers.ThreadSleep;

namespace Neolinter.CodeFixes.ThreadSleep;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ThreadSleepCodeFixProvider)), Shared]
public sealed class ThreadSleepCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use 'await Task.Delay'";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(ThreadSleepAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span, getInnermostNodeForTie: true).AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault(i => i.Span == context.Span) is not { } invocation
            || model is null
            || invocation.Parent is not ExpressionStatementSyntax
            || !ThreadSleepAnalysis.IsSleep(invocation, model, context.CancellationToken)
            || !BlockingTaskAnalysis.CanAwait(invocation, model, context.CancellationToken))
        {
            return;
        }

        // Spell the type out when 'Task' does not already mean System.Threading.Tasks.Task at this position.
        var taskInScope = model.LookupNamespacesAndTypes(invocation.SpanStart, name: "Task")
            .OfType<INamedTypeSymbol>().Any(t => t.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" && t.Arity == 0);
        var task = SyntaxFactory.ParseExpression(taskInScope ? "Task.Delay" : "System.Threading.Tasks.Task.Delay");

        var argument = invocation.ArgumentList.Arguments[0].WithNameColon(null);
        var delay = SyntaxFactory.InvocationExpression(task, invocation.ArgumentList.WithArguments(SyntaxFactory.SingletonSeparatedList(argument)));
        var replacement = SyntaxFactory.AwaitExpression(SyntaxFactory.Token(SyntaxKind.AwaitKeyword).WithTrailingTrivia(SyntaxFactory.Space), delay)
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(invocation, replacement))), equivalenceKey: ThreadSleepAnalyzer.DiagnosticId),
            context.Diagnostics);
    }
}
