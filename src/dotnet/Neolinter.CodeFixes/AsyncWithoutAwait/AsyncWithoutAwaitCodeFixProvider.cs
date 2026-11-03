using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.AsyncWithoutAwait;

namespace Neolinter.CodeFixes.AsyncWithoutAwait;

/// <summary>
/// Removes <c>async</c> from a method or local function returning <c>Task</c> or <c>Task&lt;T&gt;</c> and returns the task
/// by hand. Only offered when that cannot change behaviour: nothing in the body throws (an exception would no longer land
/// in the task) and there is no <c>using</c> or <c>try</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncWithoutAwaitCodeFixProvider)), Shared]
public sealed class AsyncWithoutAwaitCodeFixProvider : CodeFixProvider
{
    private const string Title = "Remove 'async' and return the task";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AsyncWithoutAwaitAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null
            || root.FindToken(context.Span.Start).Parent is not { } function
            || Rewrite(function, model, context.CancellationToken) is not { } rewritten)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(Title, _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(function, rewritten))), equivalenceKey: AsyncWithoutAwaitAnalyzer.DiagnosticId),
            context.Diagnostics);
    }

    private static SyntaxNode? Rewrite(SyntaxNode function, SemanticModel model, CancellationToken token)
    {
        TypeSyntax returnType;
        SyntaxTokenList modifiers;
        BlockSyntax? block;
        ArrowExpressionClauseSyntax? arrow;
        switch (function)
        {
            case MethodDeclarationSyntax m:
                (returnType, modifiers, block, arrow) = (m.ReturnType, m.Modifiers, m.Body, m.ExpressionBody);
                break;
            case LocalFunctionStatementSyntax l:
                (returnType, modifiers, block, arrow) = (l.ReturnType, l.Modifiers, l.Body, l.ExpressionBody);
                break;
            default:
                return null;
        }

        if (model.GetDeclaredSymbol(function, token) is not IMethodSymbol { ReturnType: INamedTypeSymbol { Name: "Task", Arity: <= 1 } type }
            || type.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks"
            || AsyncWithoutAwaitAnalysis.GetBody(function) is not { } body
            || AsyncWithoutAwaitAnalysis.OwnNodes(body).Any(n => n is ThrowStatementSyntax or ThrowExpressionSyntax or TryStatementSyntax or UsingStatementSyntax
                || n is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not (int)SyntaxKind.None }))
        {
            return null;
        }

        var generic = type.Arity == 1;
        TypeSyntax? typeArgument = null;
        if (generic)
        {
            var name = returnType is QualifiedNameSyntax q ? q.Right : returnType;
            if (name is not GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } generic1)
            {
                return null;
            }

            typeArgument = generic1.TypeArgumentList.Arguments[0].WithoutTrivia();
        }
        else if (arrow is not null)
        {
            // 'async Task M() => Call();' would need a block to hold the extra return.
            return null;
        }

        // Spell the type out when 'Task' does not already mean System.Threading.Tasks.Task at this position.
        var taskInScope = model.LookupNamespacesAndTypes(function.SpanStart, name: "Task")
            .OfType<INamedTypeSymbol>().Any(t => t.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks" && t.Arity == 0);
        var task = SyntaxFactory.ParseExpression(taskInScope ? "Task" : "System.Threading.Tasks.Task");

        ExpressionSyntax Wrap(ExpressionSyntax value) => SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, task,
                    SyntaxFactory.GenericName("FromResult").WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeArgument!)))),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(value.WithoutTrivia()))))
            .WithTriviaFrom(value);

        ExpressionSyntax completed = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, task, SyntaxFactory.IdentifierName("CompletedTask"));

        BlockSyntax? newBlock = null;
        ArrowExpressionClauseSyntax? newArrow = null;
        if (arrow is not null)
        {
            newArrow = arrow.WithExpression(Wrap(arrow.Expression));
        }
        else if (block is not null)
        {
            var endReachable = !generic && model.AnalyzeControlFlow(block) is { Succeeded: true, EndPointIsReachable: true };
            var returns = AsyncWithoutAwaitAnalysis.OwnNodes(block).OfType<ReturnStatementSyntax>().ToList();
            newBlock = block.ReplaceNodes(returns, (original, _) => original.Expression is { } value
                ? original.WithExpression(Wrap(value))
                : original.WithExpression(completed.WithLeadingTrivia(SyntaxFactory.Space)));

            if (endReachable)
            {
                var last = newBlock.Statements.Last();
                var multiLine = newBlock.OpenBraceToken.TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia);
                var trailing = last.GetTrailingTrivia();
                var added = SyntaxFactory.ReturnStatement(completed.WithLeadingTrivia(SyntaxFactory.Space))
                    .WithLeadingTrivia(multiLine ? SyntaxFactory.TriviaList(last.GetLeadingTrivia().LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia))) : default)
                    .WithTrailingTrivia(multiLine ? trailing.Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia)).Take(1) : trailing);
                newBlock = newBlock.ReplaceNode(last, last.WithTrailingTrivia(multiLine ? trailing : SyntaxFactory.TriviaList(SyntaxFactory.Space))).AddStatements(added);
            }
        }
        else
        {
            return null;
        }

        // Drop 'async', handing its leading trivia (indentation) to the next token.
        var asyncIndex = modifiers.IndexOf(SyntaxKind.AsyncKeyword);
        var leading = modifiers[asyncIndex].LeadingTrivia;
        modifiers = modifiers.RemoveAt(asyncIndex);
        if (asyncIndex == 0 && leading.Count > 0)
        {
            if (modifiers.Count > 0)
            {
                modifiers = modifiers.Replace(modifiers[0], modifiers[0].WithLeadingTrivia(leading.AddRange(modifiers[0].LeadingTrivia)));
            }
            else
            {
                returnType = returnType.WithLeadingTrivia(leading.AddRange(returnType.GetLeadingTrivia()));
            }
        }

        return function switch
        {
            MethodDeclarationSyntax m => m.WithModifiers(modifiers).WithReturnType(returnType).WithBody(newBlock).WithExpressionBody(newArrow),
            LocalFunctionStatementSyntax l => l.WithModifiers(modifiers).WithReturnType(returnType).WithBody(newBlock).WithExpressionBody(newArrow),
            _ => null,
        };
    }
}
