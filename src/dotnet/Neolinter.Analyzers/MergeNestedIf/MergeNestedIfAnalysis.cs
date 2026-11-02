using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.MergeNestedIf;

/// <summary>
/// An <c>if</c> without <c>else</c> whose only statement is another <c>if</c> without <c>else</c>, possibly several
/// levels deep. <see cref="Ifs"/> starts with the outermost one; the conditions can be joined with <c>&amp;&amp;</c>.
/// </summary>
internal sealed class MergeNestedIfChain(IReadOnlyList<IfStatementSyntax> ifs)
{
    public IReadOnlyList<IfStatementSyntax> Ifs { get; } = ifs;

    public IfStatementSyntax Outer => Ifs[0];

    public IfStatementSyntax Innermost => Ifs[Ifs.Count - 1];
}

/// <summary>Detection shared by the NLR0008 analyzer and its code fix.</summary>
internal static class MergeNestedIfAnalysis
{
    /// <summary>
    /// The chain starting at <paramref name="outer"/> (at least two levels), or null when there is none or when
    /// <paramref name="outer"/> is itself the inner level of a chain that starts higher up (it is reported there).
    /// </summary>
    public static MergeNestedIfChain? Analyze(IfStatementSyntax outer, SemanticModel model, CancellationToken cancellationToken)
    {
        var chain = BuildChain(outer, model, cancellationToken);
        if (chain is null)
        {
            return null;
        }

        return GetEnclosingIf(outer) is { } parent && BuildChain(parent, model, cancellationToken)?.Ifs.Contains(outer) == true
            ? null
            : chain;
    }

    private static MergeNestedIfChain? BuildChain(IfStatementSyntax outer, SemanticModel model, CancellationToken cancellationToken)
    {
        var ifs = new List<IfStatementSyntax> { outer };
        var names = new HashSet<string>(IdentifierNames(outer.Condition));
        var current = outer;
        while (GetInner(current) is { } inner && IsMergeable(current, inner, names, model, cancellationToken))
        {
            ifs.Add(inner);
            names.UnionWith(IdentifierNames(inner.Condition));
            current = inner;
        }

        return ifs.Count > 1 ? new MergeNestedIfChain(ifs) : null;
    }

    /// <summary>The <c>if</c> that is the only statement of <paramref name="outer"/> (braces or not), when it has no <c>else</c>.</summary>
    private static IfStatementSyntax? GetInner(IfStatementSyntax outer)
    {
        if (outer.Else is not null)
        {
            return null;
        }

        var statement = outer.Statement is BlockSyntax { Statements.Count: 1 } block ? block.Statements[0] : outer.Statement;
        return statement is IfStatementSyntax { Else: null } inner ? inner : null;
    }

    // The 'if' directly around this one: its statement, or the single statement of its block.
    private static IfStatementSyntax? GetEnclosingIf(IfStatementSyntax ifStatement)
    {
        var parent = ifStatement.Parent;
        if (parent is BlockSyntax { Statements.Count: 1 })
        {
            parent = parent.Parent;
        }

        return parent as IfStatementSyntax;
    }

    private static bool IsMergeable(IfStatementSyntax outer, IfStatementSyntax inner, HashSet<string> outerNames, SemanticModel model, CancellationToken cancellationToken)
    {
        if (outer.ContainsDirectives || outer.ContainsDiagnostics
            || HasLostComment(outer, inner)
            || !IsBool(outer.Condition, model, cancellationToken)
            || !IsBool(inner.Condition, model, cancellationToken))
        {
            return false;
        }

        // 'if (u == 1) { if (o is T u) ... }' is fine (u is a field in the first condition, a local in the second);
        // 'u == 1 && o is T u' is not, because the local is in scope in the whole condition.
        return !inner.Condition.DescendantNodesAndSelf()
            .OfType<SingleVariableDesignationSyntax>()
            .Any(designation => outerNames.Contains(designation.Identifier.ValueText));
    }

    // 'a && b' on a type with user-defined '&', 'true' and 'false' is not 'if (a) if (b)'; so only plain bool.
    private static bool IsBool(ExpressionSyntax condition, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetTypeInfo(condition, cancellationToken).Type?.SpecialType == SpecialType.System_Boolean;

    private static IEnumerable<string> IdentifierNames(SyntaxNode node) =>
        node.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText);

    /// <summary>
    /// The fix drops the tokens between the two conditions: a comment in one of them would be lost. Trivia that the
    /// fix keeps (the end of the inner condition's line, what follows the outer closing brace) is not looked at.
    /// </summary>
    private static bool HasLostComment(IfStatementSyntax outer, IfStatementSyntax inner)
    {
        var lost = new List<SyntaxToken>
        {
            outer.Condition.GetLastToken(),
            outer.CloseParenToken,
            inner.IfKeyword,
            inner.OpenParenToken,
            inner.Condition.GetFirstToken(),
            inner.Condition.GetLastToken(),
            inner.CloseParenToken,
        };

        if (outer.Statement is BlockSyntax block)
        {
            lost.Add(block.OpenBraceToken);
            lost.Add(block.CloseBraceToken);
        }

        // Kept: the trailing trivia of the inner ')' (moved onto the outer ')'), and the trailing trivia of the
        // outer closing brace (moved onto the end of the inner statement).
        return lost.Any(token => HasComment(token.LeadingTrivia) || (HasComment(token.TrailingTrivia) && token != inner.CloseParenToken && token != outer.GetLastToken()))
            || (outer.Statement is BlockSyntax && HasComment(inner.GetLastToken().TrailingTrivia));
    }

    private static bool HasComment(SyntaxTriviaList trivia) =>
        trivia.Any(t => t.IsDirective
            || t.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
}
