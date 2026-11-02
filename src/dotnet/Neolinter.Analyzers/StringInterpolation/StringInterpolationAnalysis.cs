using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.StringInterpolation;

/// <summary>
/// Shared by the NLR0021 analyzer and code fix. <c>"a" + x + "b"</c> becomes <c>$"a{x}b"</c>. Only a chain of string
/// concatenations is converted: a numeric <c>+</c> on its left stays one operand (<c>1 + 2 + "a"</c> gives
/// <c>$"{1 + 2}a"</c>). Not reported: constants, verbatim, raw and UTF-8 literals, interpolated operands, comments, chains
/// over several lines, expression trees and queries, operands that cannot be written in a hole, a language older than
/// C# 6, and a chain that holds another chain in an operand (that inner chain goes first).
/// </summary>
internal static class StringInterpolationAnalysis
{
    /// <summary>The interpolated string for the chain whose outermost <c>+</c> is <paramref name="node"/>, or <c>null</c>.</summary>
    public static string? Analyze(BinaryExpressionSyntax node, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!node.IsKind(SyntaxKind.AddExpression)
            || node.ContainsDiagnostics
            || node.ContainsDirectives
            || node.SyntaxTree.Options is not CSharpParseOptions options
            || options.LanguageVersion.MapSpecifiedToEffectiveVersion() < LanguageVersion.CSharp6
            || !IsConcatenation(node, model, cancellationToken)
            || (node.Parent is BinaryExpressionSyntax parent && parent.IsKind(SyntaxKind.AddExpression) && parent.Left == node
                && IsConcatenation(parent, model, cancellationToken))
            || model.GetConstantValue(node, cancellationToken).HasValue
            || node.GetLocation().GetLineSpan() is { StartLinePosition.Line: var start, EndLinePosition.Line: var end } && start != end
            || node.DescendantTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia))
            || InExpressionTree(node, model, cancellationToken))
        {
            return null;
        }

        var operands = new List<ExpressionSyntax>();
        ExpressionSyntax current = node;
        while (current is BinaryExpressionSyntax add && add.IsKind(SyntaxKind.AddExpression) && IsConcatenation(add, model, cancellationToken))
        {
            operands.Insert(0, add.Right);
            current = add.Left;
        }

        operands.Insert(0, current);

        var hasLiteral = false;
        var hasHole = false;
        var text = new StringBuilder("$\"");
        foreach (var operand in operands)
        {
            if (operand is LiteralExpressionSyntax { Token: var token } literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                // Only plain "..." literals: verbatim, raw and UTF-8 ones keep their own rules for quotes and line breaks.
                if (!token.Text.StartsWith("\"", StringComparison.Ordinal) || token.Text.StartsWith("\"\"\"", StringComparison.Ordinal)
                    || !token.Text.EndsWith("\"", StringComparison.Ordinal))
                {
                    return null;
                }

                hasLiteral = true;
                text.Append(token.Text.Substring(1, token.Text.Length - 2).Replace("{", "{{").Replace("}", "}}"));
                continue;
            }

            if (!TryHole(operand, model, cancellationToken, out var hole))
            {
                return null;
            }

            hasHole |= !model.GetConstantValue(operand, cancellationToken).HasValue;
            text.Append('{').Append(hole).Append('}');
        }

        if (!hasLiteral || !hasHole)
        {
            return null;
        }

        // A chain nested in an operand is converted first: the two fixes would overlap.
        foreach (var operand in operands)
        {
            if (operand.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Any(inner => inner != node && Analyze(inner, model, cancellationToken) is not null))
            {
                return null;
            }
        }

        return text.Append('"').ToString();
    }

    private static bool IsConcatenation(BinaryExpressionSyntax node, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetTypeInfo(node, cancellationToken).Type?.SpecialType == SpecialType.System_String
        && model.GetSymbolInfo(node, cancellationToken).Symbol is IMethodSymbol { MethodKind: MethodKind.BuiltinOperator };

    private static bool TryHole(ExpressionSyntax operand, SemanticModel model, CancellationToken cancellationToken, out string hole)
    {
        hole = "";
        var type = model.GetTypeInfo(operand, cancellationToken).Type;
        if (operand is InterpolatedStringExpressionSyntax or AnonymousFunctionExpressionSyntax or QueryExpressionSyntax or SwitchExpressionSyntax
            || type is null or IErrorTypeSymbol or IPointerTypeSymbol || type.IsRefLikeType || type.SpecialType == SpecialType.System_Void
            || operand.ContainsDirectives)
        {
            return false;
        }

        var source = operand.WithoutTrivia().ToString();
        if (source.Contains('\n') || source.Contains('{') || source.Contains('}'))
        {
            return false;
        }

        // A ':' or ',' outside brackets would start a format or an alignment: that includes 'a ? b : c' and 'F<A, B>(x)'.
        var depth = 0;
        foreach (var token in operand.DescendantTokens())
        {
            switch (token.Kind())
            {
                case SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken:
                    depth++;
                    break;
                case SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken:
                    depth--;
                    break;
                case SyntaxKind.ColonToken or SyntaxKind.CommaToken when depth == 0:
                    source = $"({source})";
                    hole = source;
                    return true;
            }
        }

        hole = source;
        return true;
    }

    private static bool InExpressionTree(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is QueryExpressionSyntax)
            {
                return true;
            }

            if (ancestor is AnonymousFunctionExpressionSyntax
                && model.GetTypeInfo(ancestor, cancellationToken).ConvertedType?.OriginalDefinition.ToDisplayString() == "System.Linq.Expressions.Expression<TDelegate>")
            {
                return true;
            }
        }

        return false;
    }
}
