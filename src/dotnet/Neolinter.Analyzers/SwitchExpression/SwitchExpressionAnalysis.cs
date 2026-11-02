using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.SwitchExpression;

/// <summary>One arm of the switch expression, as source text: <c>pattern [when condition] =&gt; value</c>.</summary>
internal sealed class SwitchExpressionArm(string pattern, string? when, string value)
{
    public string Pattern { get; } = pattern;

    public string? When { get; } = when;

    public string Value { get; } = value;
}

internal sealed class SwitchExpressionCandidate(SwitchStatementSyntax statement, string? target, string governing, ImmutableArray<SwitchExpressionArm> arms)
{
    public SwitchStatementSyntax Statement { get; } = statement;

    /// <summary>The assigned target, or null when the sections return.</summary>
    public string? Target { get; } = target;

    public string Governing { get; } = governing;

    public ImmutableArray<SwitchExpressionArm> Arms { get; } = arms;
}

/// <summary>
/// Shared by the NLR0019 analyzer and code fix. A <c>switch</c> statement whose sections each return a value, or each assign the same
/// variable, with a <c>default</c> section last, becomes a switch expression. Anything that could change behaviour is not reported:
/// a missing <c>default</c> (the expression would throw where the statement does nothing), several statements in a section, comments,
/// multi-line parts, or a typing that differs between the statement and the expression.
/// </summary>
internal static class SwitchExpressionAnalysis
{
    public static SwitchExpressionCandidate? Analyze(SwitchStatementSyntax sw, SemanticModel model, CancellationToken cancellationToken)
    {
        if (sw.SyntaxTree.Options is not CSharpParseOptions options || options.LanguageVersion.MapSpecifiedToEffectiveVersion() < LanguageVersion.CSharp8
            || sw.Sections.Count < 2
            || sw.WithoutTrivia().ContainsDirectives
            || sw.WithoutTrivia().DescendantTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            || sw.Expression.DescendantNodesAndSelf().Any(n => n.IsKind(SyntaxKind.AwaitExpression) || IsMultiLine(n))
            || sw.Sections.Any(s => s.DescendantNodes().OfType<SwitchStatementSyntax>().Any()))
        {
            return null;
        }

        var orAllowed = options.LanguageVersion.MapSpecifiedToEffectiveVersion() >= LanguageVersion.CSharp9;

        // 'default' last, and nothing else on it: '_' ahead of other arms would hide them.
        var last = sw.Sections[sw.Sections.Count - 1];
        var hasDefault = last.Labels.Count == 1 && last.Labels[0] is DefaultSwitchLabelSyntax;
        if (!hasDefault && !IsBoolSwitch(sw, model, cancellationToken))
        {
            return null;
        }

        var arms = ImmutableArray.CreateBuilder<SwitchExpressionArm>();
        var values = new List<ExpressionSyntax>();
        ExpressionSyntax? target = null;
        bool? returns = null;
        foreach (var section in sw.Sections)
        {
            if (!TryGetBody(section, out var statements) || TryGetArm(section, orAllowed) is not { } head)
            {
                return null;
            }

            string value;
            if (statements.Count == 1 && statements[0] is ThrowStatementSyntax { Expression: { } thrown })
            {
                value = "throw " + thrown;
            }
            else if (statements.Count == 1 && statements[0] is ReturnStatementSyntax { Expression: { } returned and not RefExpressionSyntax } && returns != false)
            {
                returns = true;
                values.Add(returned);
                value = returned.ToString();
            }
            else if (statements.Count == 2 && statements[1] is BreakStatementSyntax
                && statements[0] is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && returns != true && IsSimpleTarget(assignment.Left) && (target is null || SyntaxFactory.AreEquivalent(target, assignment.Left)))
            {
                returns = false;
                target = assignment.Left;
                values.Add(assignment.Right);
                value = assignment.Right.ToString();
            }
            else
            {
                return null;
            }

            if (IsMultiLine(statements[0]) || section.Labels.Any(IsMultiLine))
            {
                return null;
            }

            arms.Add(new SwitchExpressionArm(head.Pattern, head.When, value));
        }

        if (returns is null || !TypingIsUnchanged(sw, arms, values, model, orAllowed, cancellationToken))
        {
            return null;
        }

        return new SwitchExpressionCandidate(sw, target?.ToString(), Governing(sw), arms.ToImmutable());
    }

    // The statements of a section, looking through one block; a block with locals or anything else is not an arm.
    private static bool TryGetBody(SwitchSectionSyntax section, out SyntaxList<StatementSyntax> statements)
    {
        statements = section.Statements;
        if (statements.Count == 1 && statements[0] is BlockSyntax block)
        {
            statements = block.Statements;
        }

        return statements.Count is 1 or 2;
    }

    private static (string Pattern, string? When)? TryGetArm(SwitchSectionSyntax section, bool orAllowed)
    {
        if (section.Labels.Count > 1 && !orAllowed)
        {
            return null;
        }

        var patterns = new List<string>();
        string? when = null;
        foreach (var label in section.Labels)
        {
            switch (label)
            {
                case DefaultSwitchLabelSyntax when section.Labels.Count == 1:
                    return ("_", null);
                case CaseSwitchLabelSyntax constant:
                    patterns.Add(constant.Value.ToString());
                    break;
                case CasePatternSwitchLabelSyntax pattern:
                    // 'a when c or b' cannot be written, and an alternative cannot declare a variable.
                    if (section.Labels.Count > 1
                        && (pattern.WhenClause is not null
                            || pattern.Pattern.DescendantNodesAndSelf().Any(n => n is DeclarationPatternSyntax or VarPatternSyntax or RecursivePatternSyntax { Designation: not null })))
                    {
                        return null;
                    }

                    when = pattern.WhenClause?.Condition.ToString();
                    patterns.Add(pattern.Pattern.ToString());
                    break;
                default:
                    return null;
            }
        }

        return (string.Join(" or ", patterns), when);
    }

    private static bool IsMultiLine(SyntaxNode node) =>
        node.ToString().Contains('\n');

    private static string Governing(SwitchStatementSyntax sw) =>
        NeedsParentheses(sw.Expression) ? "(" + sw.Expression + ")" : sw.Expression.ToString();

    // 'a + b switch { ... }' reads as 'a + (b switch { ... })'.
    private static bool NeedsParentheses(ExpressionSyntax expression) =>
        expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax
            or LiteralExpressionSyntax or TupleExpressionSyntax or ParenthesizedExpressionSyntax or ThisExpressionSyntax or TypeOfExpressionSyntax
            or DefaultExpressionSyntax or SizeOfExpressionSyntax or CheckedExpressionSyntax);

    // Evaluated once instead of once per section: only names and 'this.' members, never calls or indexers.
    private static bool IsSimpleTarget(ExpressionSyntax target) =>
        target is IdentifierNameSyntax
        || target is MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression, Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax };

    // 'case true' and 'case false', both and nothing else, without guards.
    private static bool IsBoolSwitch(SwitchStatementSyntax sw, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetTypeInfo(sw.Expression, cancellationToken).Type?.SpecialType != SpecialType.System_Boolean || sw.Sections.Count != 2)
        {
            return false;
        }

        var seen = new HashSet<bool>();
        foreach (var section in sw.Sections)
        {
            if (section.Labels.Count != 1 || section.Labels[0] is not CaseSwitchLabelSyntax { Value: LiteralExpressionSyntax literal }
                || !literal.IsKind(SyntaxKind.TrueLiteralExpression) && !literal.IsKind(SyntaxKind.FalseLiteralExpression))
            {
                return false;
            }

            seen.Add(literal.IsKind(SyntaxKind.TrueLiteralExpression));
        }

        return seen.Count == 2;
    }

    // The statement converts each value to the target type; the expression must do the same. A natural type of the expression
    // (the best common type of the arms) wins over the target type, so it has to be the target type itself. Without a natural
    // type, C# 9 converts each arm to the target type, as the statement did; C# 8 has no target typing and gives an error.
    private static bool TypingIsUnchanged(SwitchStatementSyntax sw, ImmutableArray<SwitchExpressionArm>.Builder arms, List<ExpressionSyntax> values, SemanticModel model, bool targetTyping, CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            return false;
        }

        ITypeSymbol? target = null;
        foreach (var value in values)
        {
            var converted = model.GetTypeInfo(value, cancellationToken).ConvertedType;
            if (converted is null or { TypeKind: TypeKind.Error or TypeKind.Dynamic } || (target is not null && !SymbolEqualityComparer.Default.Equals(target, converted)))
            {
                return false;
            }

            target = converted;
        }

        var text = Governing(sw) + " switch { "
            + string.Join(", ", arms.Select(a => a.Pattern + (a.When is null ? "" : " when " + a.When) + " => " + a.Value)) + " }";
        var natural = model.GetSpeculativeTypeInfo(sw.SpanStart, SyntaxFactory.ParseExpression(text), SpeculativeBindingOption.BindAsExpression).Type;
        if (natural is { TypeKind: not TypeKind.Error })
        {
            return SymbolEqualityComparer.Default.Equals(natural, target);
        }

        return targetTyping;
    }
}
