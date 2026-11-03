using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Neolinter.Analyzers.StringInterpolation;

namespace Neolinter.Analyzers.StringFormat;

/// <summary>
/// Shared by the NLR0022 analyzer and code fix. <c>string.Format("a{0,5:F2}b", x)</c> becomes <c>$"a{x,5:F2}b"</c>. The format
/// must be a plain string literal; every argument must be used and every placeholder must have an argument. An argument used
/// several times must be an identifier, a member access chain or a literal, and arguments with possible side effects must
/// stay in their order of use. Not reported: culture overloads, an array passed as the arguments, named or <c>ref</c>
/// arguments, verbatim, raw and UTF-8 formats, unicode escapes, other placeholder syntax, comments, calls over several lines,
/// expression trees and queries, arguments that cannot be written in a hole, a language older than C# 6, and a call that
/// holds another reportable call in an argument (that inner call goes first).
/// </summary>
internal static class StringFormatAnalysis
{
    private static readonly Regex Placeholder = new(@"\G\{(\d{1,4})(?:,(-?\d{1,4}))?(?::([^{}""\\\r\n]+))?\}", RegexOptions.CultureInvariant);

    /// <summary>The interpolated string for the call <paramref name="node"/>, or <c>null</c>.</summary>
    public static string? Analyze(InvocationExpressionSyntax node, SemanticModel model, CancellationToken cancellationToken)
    {
        var arguments = node.ArgumentList.Arguments;
        if (arguments.Count < 2
            || node.ContainsDiagnostics
            || node.ContainsDirectives
            || node.Expression is not (MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Format" } or IdentifierNameSyntax { Identifier.ValueText: "Format" })
            || node.SyntaxTree.Options is not CSharpParseOptions options
            || options.LanguageVersion.MapSpecifiedToEffectiveVersion() < LanguageVersion.CSharp6
            || arguments[0].Expression is not LiteralExpressionSyntax { Token: var token } literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression)
            || arguments.Any(a => a.NameColon is not null || !a.RefKindKeyword.IsKind(SyntaxKind.None))
            || model.GetSymbolInfo(node, cancellationToken).Symbol is not IMethodSymbol { Name: "Format", ContainingType.SpecialType: SpecialType.System_String, ReturnType.SpecialType: SpecialType.System_String } method
            || !IsPlainOverload(method)
            || (arguments.Count == 2 && model.GetTypeInfo(arguments[1].Expression, cancellationToken).Type is IArrayTypeSymbol)
            || node.GetLocation().GetLineSpan() is { StartLinePosition.Line: var start, EndLinePosition.Line: var end } && start != end
            || node.DescendantTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia))
            || StringInterpolationAnalysis.InExpressionTree(node, model, cancellationToken))
        {
            return null;
        }

        // Only plain "..." formats: verbatim, raw and UTF-8 ones keep their own rules, and a unicode escape could make a brace.
        var raw = token.Text;
        if (!raw.StartsWith("\"", StringComparison.Ordinal) || raw.StartsWith("\"\"\"", StringComparison.Ordinal) || !raw.EndsWith("\"", StringComparison.Ordinal)
            || raw.Contains("\\u") || raw.Contains("\\U") || raw.Contains("\\x"))
        {
            return null;
        }

        var holes = new string[arguments.Count - 1];
        for (var i = 1; i < arguments.Count; i++)
        {
            if (!StringInterpolationAnalysis.TryHole(arguments[i].Expression, model, cancellationToken, out holes[i - 1]!))
            {
                return null;
            }
        }

        var format = raw.Substring(1, raw.Length - 2);
        var text = new StringBuilder("$\"");
        var order = new List<int>();
        for (var i = 0; i < format.Length;)
        {
            var c = format[i];
            if ((c == '{' || c == '}') && i + 1 < format.Length && format[i + 1] == c)
            {
                text.Append(c).Append(c);
                i += 2;
            }
            else if (c == '{')
            {
                var match = Placeholder.Match(format, i);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out var index) || index >= holes.Length)
                {
                    return null;
                }

                order.Add(index);
                text.Append('{').Append(holes[index]).Append(match.Groups[2].Success ? "," + match.Groups[2].Value : "")
                    .Append(match.Groups[3].Success ? ":" + match.Groups[3].Value : "").Append('}');
                i += match.Length;
            }
            else if (c == '}')
            {
                return null;
            }
            else
            {
                text.Append(c);
                i++;
            }
        }

        if (order.Distinct().Count() != holes.Length)
        {
            return null;
        }

        // The arguments are evaluated once, in order; the interpolated string evaluates each hole where it stands.
        var pure = arguments.Skip(1).Select(a => IsPure(a.Expression, model, cancellationToken)).ToArray();
        if (!pure.All(p => p) && !order.SequenceEqual(Enumerable.Range(0, holes.Length)))
        {
            return null;
        }

        // A call nested in an argument is converted first: the two fixes would overlap.
        foreach (var argument in arguments.Skip(1))
        {
            if (argument.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(inner => Analyze(inner, model, cancellationToken) is not null))
            {
                return null;
            }
        }

        return text.Append('"').ToString();
    }

    // string.Format(string, object[]) and the (string, object, ...) overloads; not culture, span or composite ones.
    private static bool IsPlainOverload(IMethodSymbol method) =>
        method.Parameters.Length >= 2
        && method.Parameters[0].Type.SpecialType == SpecialType.System_String
        && method.Parameters.Skip(1).All(p => p.Type.SpecialType == SpecialType.System_Object || p.Type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Object });

    private static bool IsPure(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetConstantValue(expression, cancellationToken).HasValue
        || expression switch
        {
            IdentifierNameSyntax or ThisExpressionSyntax or LiteralExpressionSyntax => true,
            MemberAccessExpressionSyntax member when member.IsKind(SyntaxKind.SimpleMemberAccessExpression) => IsPure(member.Expression, model, cancellationToken),
            _ => false,
        };
}
