using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.Rethrow;

/// <summary>
/// Shared by the NLR0023 analyzer and code fix. <c>throw e;</c> is reported when <c>e</c> is the variable of the catch clause
/// that directly encloses the statement, so that <c>throw;</c> rethrows the same exception. Not reported: a throw inside a
/// lambda, anonymous method or local function, inside a <c>finally</c> within the catch, inside a nested catch clause, or
/// when the variable is written anywhere in the catch block.
/// </summary>
internal static class RethrowAnalysis
{
    public static bool IsReportable(ThrowStatementSyntax node, SemanticModel model, CancellationToken cancellationToken)
    {
        if (node.Expression is not IdentifierNameSyntax name
            || model.GetSymbolInfo(name, cancellationToken).Symbol is not ILocalSymbol local
            || local.DeclaringSyntaxReferences.Length != 1
            || local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not CatchDeclarationSyntax declaration)
        {
            return false;
        }

        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case CatchClauseSyntax clause:
                    return clause.Declaration == declaration
                        && model.AnalyzeDataFlow(clause.Block) is { Succeeded: true } flow
                        && !flow.WrittenInside.Contains(local, SymbolEqualityComparer.Default);
                case FinallyClauseSyntax:
                case AnonymousFunctionExpressionSyntax:
                case LocalFunctionStatementSyntax:
                    return false;
            }
        }

        return false;
    }
}
