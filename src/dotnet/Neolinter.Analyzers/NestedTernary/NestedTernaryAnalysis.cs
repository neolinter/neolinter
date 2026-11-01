using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.NestedTernary;

/// <summary>
/// A conditional expression is reported when it contains another one and is not itself inside one. A lambda,
/// anonymous method or local function is a boundary: what sits inside it is judged on its own.
/// </summary>
internal static class NestedTernaryAnalysis
{
    public static bool IsReported(ConditionalExpressionSyntax conditional) => !IsInsideConditional(conditional) && ContainsConditional(conditional);

    private static bool ContainsConditional(ConditionalExpressionSyntax conditional) =>
        conditional.DescendantNodes(node => !IsBoundary(node)).Any(node => node is ConditionalExpressionSyntax);

    private static bool IsInsideConditional(ConditionalExpressionSyntax conditional)
    {
        foreach (var ancestor in conditional.Ancestors())
        {
            if (ancestor is ConditionalExpressionSyntax)
            {
                return true;
            }

            // Statements only appear below a lambda or a member: nothing above can be a conditional expression.
            if (IsBoundary(ancestor) || ancestor is StatementSyntax)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsBoundary(SyntaxNode node) => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;
}
