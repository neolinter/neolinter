using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.AsyncWithoutAwait;

/// <summary>Detection shared by the NLR0027 analyzer and its code fix.</summary>
internal static class AsyncWithoutAwaitAnalysis
{
    /// <summary>
    /// True for an <c>async</c> method, local function, lambda or anonymous method that never awaits (<c>await</c>,
    /// <c>await foreach</c>, <c>await using</c>), other than <c>async void</c>, an iterator, an empty body or a body that
    /// only throws. <paramref name="location"/> is where to report.
    /// </summary>
    internal static bool IsReportable(SyntaxNode function, SemanticModel model, CancellationToken token, out Location location)
    {
        location = Location.None;
        SyntaxToken asyncKeyword;
        SyntaxToken at;
        bool returnsVoid;

        switch (function)
        {
            case MethodDeclarationSyntax method:
                asyncKeyword = AsyncOf(method.Modifiers);
                at = method.Identifier;
                returnsVoid = model.GetDeclaredSymbol(method, token) is not IMethodSymbol { ReturnsVoid: false };
                break;
            case LocalFunctionStatementSyntax local:
                asyncKeyword = AsyncOf(local.Modifiers);
                at = local.Identifier;
                returnsVoid = model.GetDeclaredSymbol(local, token) is not IMethodSymbol { ReturnsVoid: false };
                break;
            case AnonymousFunctionExpressionSyntax anonymous:
                asyncKeyword = anonymous.AsyncKeyword;
                at = asyncKeyword;
                returnsVoid = model.GetTypeInfo(anonymous, token).ConvertedType is not INamedTypeSymbol { DelegateInvokeMethod.ReturnsVoid: false };
                break;
            default:
                return false;
        }

        if (asyncKeyword.IsKind(SyntaxKind.None) || returnsVoid || GetBody(function) is not { } body || IsStub(body))
        {
            return false;
        }

        foreach (var node in OwnNodes(body))
        {
            if (node is AwaitExpressionSyntax or YieldStatementSyntax
                || node is CommonForEachStatementSyntax { AwaitKeyword.RawKind: not (int)SyntaxKind.None }
                || node is UsingStatementSyntax { AwaitKeyword.RawKind: not (int)SyntaxKind.None }
                || node is LocalDeclarationStatementSyntax { AwaitKeyword.RawKind: not (int)SyntaxKind.None })
            {
                return false;
            }
        }

        location = at.GetLocation();
        return true;
    }

    internal static SyntaxNode? GetBody(SyntaxNode function) => function switch
    {
        MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody?.Expression,
        LocalFunctionStatementSyntax l => (SyntaxNode?)l.Body ?? l.ExpressionBody?.Expression,
        AnonymousFunctionExpressionSyntax a => a.Body,
        _ => null,
    };

    /// <summary>The nodes of the function itself, not those of the lambdas and local functions inside it.</summary>
    internal static IEnumerable<SyntaxNode> OwnNodes(SyntaxNode body) => body.DescendantNodesAndSelf(n => !IsFunction(n));

    private static bool IsFunction(SyntaxNode node) => node is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;

    private static SyntaxToken AsyncOf(SyntaxTokenList modifiers) => modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.AsyncKeyword));

    /// <summary>An empty body (a deliberate stub) or one that only throws: <c>async</c> is what turns the exception into a faulted task.</summary>
    private static bool IsStub(SyntaxNode body) => body switch
    {
        BlockSyntax { Statements.Count: 0 } => true,
        BlockSyntax { Statements.Count: 1 } block => block.Statements[0] is ThrowStatementSyntax,
        ThrowExpressionSyntax => true,
        _ => false,
    };
}
