using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Neolinter.CodeFixes.NestedTernary;

/// <summary>
/// Rewrites <c>return c1 ? a : c2 ? b : c;</c>, <c>x = c1 ? a : c2 ? b : c;</c> and <c>T x = c1 ? a : c2 ? b : c;</c>
/// into an <c>if / else if / else</c> chain. Every doubt returns <c>null</c> (no fix): see <c>rules/NLR0007.yaml</c>.
/// </summary>
internal static class NestedTernaryRewriter
{
    private enum Context { Return, Assignment, Declaration }

    /// <summary>The new root, or <c>null</c> when the expression is not in a supported, provably equivalent shape.</summary>
    /// <param name="preferBraces">The <c>csharp_prefer_braces</c> value when it is <c>true</c> or <c>false</c>, else <c>null</c>.</param>
    /// <param name="whenMultiline">The <c>csharp_prefer_braces</c> value is <c>when_multiline</c>.</param>
    public static SyntaxNode? TryRewrite(SyntaxNode root, SemanticModel model, ConditionalExpressionSyntax conditional, bool? preferBraces, bool whenMultiline, CancellationToken cancellationToken)
    {
        var tree = conditional.SyntaxTree;
        var text = tree.GetText(cancellationToken);

        if (!TryGetShape(conditional, model, cancellationToken, out var statement, out var context, out var prefix, out var allowedSpans, out var declaration)
            || statement.Parent is not (BlockSyntax or SwitchSectionSyntax) || LineIndent(statement.SpanStart, text) is not { } indent)
        {
            return null;
        }

        // Flat chain along the false branches; any other conditional (in a condition, a true branch, a lambda) means no fix.
        var conditions = new List<ExpressionSyntax>();
        var bodies = new List<ExpressionSyntax>();
        var current = conditional;
        while (true)
        {
            conditions.Add(current.Condition);
            bodies.Add(current.WhenTrue);
            if (Unwrap(current.WhenFalse) is ConditionalExpressionSyntax next)
            {
                current = next;
                continue;
            }

            bodies.Add(current.WhenFalse);
            break;
        }

        if (conditional.DescendantNodesAndSelf().OfType<ConditionalExpressionSyntax>().Count() != conditions.Count)
        {
            return null;
        }

        // Splitting 'x = ...;' or 'T x = ...;' shrinks the scope of variables declared in a condition; a 'return' does not.
        if (context != Context.Return && conditional.DescendantNodes().Any(n => n is SingleVariableDesignationSyntax or ParenthesizedVariableDesignationSyntax))
        {
            return null;
        }

        allowedSpans.AddRange(conditions.Concat(bodies).Select(e => e.Span));
        if (HasLostTrivia(statement, allowedSpans) || !HasSameTypeEverywhere(conditional, bodies, model, cancellationToken))
        {
            return null;
        }

        var eol = text.ToString().Contains("\r\n") ? "\r\n" : "\n";
        var unit = IndentUnit(statement.Parent, indent, text);
        var statements = bodies.Select(b => prefix + b + ";").ToList();
        var braces = preferBraces ?? (whenMultiline ? statements.Any(s => s.Contains('\n')) : ExistingStyleUsesBraces(root));
        var chain = BuildChain(conditions.Select(c => c.ToString()).ToList(), statements, indent, unit, eol, braces, IsAllman(statement));

        var options = (CSharpParseOptions)tree.Options;
        var replacement = new List<StatementSyntax>();
        if (declaration is not null)
        {
            replacement.Add(SyntaxFactory.ParseStatement(declaration, options: options));
            replacement.Add(SyntaxFactory.ParseStatement(chain, options: options).WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia(eol + indent)));
        }
        else
        {
            replacement.Add(SyntaxFactory.ParseStatement(chain, options: options));
        }

        replacement[0] = replacement[0].WithLeadingTrivia(statement.GetLeadingTrivia());
        replacement[replacement.Count - 1] = replacement[replacement.Count - 1].WithTrailingTrivia(statement.GetTrailingTrivia());
        return root.ReplaceNode(statement, replacement);
    }

    /// <param name="prefix">What precedes each branch value: <c>return </c>, <c>x = </c>.</param>
    /// <param name="declaration">For <c>T x = ...</c>: the declaration <c>T x;</c> written before the chain.</param>
    private static bool TryGetShape(
        ConditionalExpressionSyntax conditional, SemanticModel model, CancellationToken cancellationToken,
        out StatementSyntax statement, out Context context, out string prefix, out List<TextSpan> allowedSpans, out string? declaration)
    {
        statement = null!;
        context = Context.Return;
        prefix = "";
        allowedSpans = [];
        declaration = null;

        switch (conditional.Parent)
        {
            case ReturnStatementSyntax @return:
                statement = @return;
                prefix = "return ";
                return true;

            case AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression, Parent: ExpressionStatementSyntax expressionStatement } assignment
                when assignment.Right == conditional && IsStableTarget(assignment.Left, model, cancellationToken):
                statement = expressionStatement;
                context = Context.Assignment;
                prefix = assignment.Left + " = ";
                allowedSpans.Add(assignment.Left.Span);
                return true;

            // 'T x = ...' only: 'var' has no type to write, and const, 'using', 'ref' and several declarators are left alone.
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { ArgumentList: null } declarator }
                when declarator.Parent is VariableDeclarationSyntax { Variables.Count: 1 } variableDeclaration
                    && variableDeclaration.Parent is LocalDeclarationStatementSyntax { Modifiers.Count: 0, UsingKeyword.RawKind: 0, AwaitKeyword.RawKind: 0 } local
                    && !variableDeclaration.Type.IsVar && variableDeclaration.Type is not (RefTypeSyntax or ScopedTypeSyntax):
                statement = local;
                context = Context.Declaration;
                prefix = declarator.Identifier.Text + " = ";
                declaration = variableDeclaration.Type + " " + declarator.Identifier.Text + ";";
                allowedSpans.Add(variableDeclaration.Type.Span);
                return true;

            default:
                return false;
        }
    }

    // The target is evaluated before the right side in 'x = ...' but after the conditions in the rewrite: it must not matter.
    private static bool IsStableTarget(ExpressionSyntax target, SemanticModel model, CancellationToken cancellationToken) => target switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression, Expression: ThisExpressionSyntax } => true,
        MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression, Expression: IdentifierNameSyntax receiver } =>
            model.GetSymbolInfo(receiver, cancellationToken).Symbol is ILocalSymbol or IParameterSymbol or INamedTypeSymbol,
        _ => false,
    };

    // Comments or directives that are not inside an operand would vanish (for instance next to '?' or ':').
    private static bool HasLostTrivia(StatementSyntax statement, List<TextSpan> allowedSpans) =>
        statement.DescendantTrivia().Any(trivia =>
            trivia.SpanStart >= statement.SpanStart && trivia.Span.End <= statement.Span.End
            && (trivia.IsDirective
                || ((trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                    && !allowedSpans.Any(span => span.Contains(trivia.Span)))));

    // Each branch is converted on its own after the rewrite: that equals converting the whole expression only when every
    // branch already has the type of the whole. Target-typed ('c ? 1 : null') and mixed ('c ? 1 : 2.5') cases have none.
    private static bool HasSameTypeEverywhere(ConditionalExpressionSyntax conditional, List<ExpressionSyntax> bodies, SemanticModel model, CancellationToken cancellationToken)
    {
        var type = model.GetTypeInfo(conditional, cancellationToken).Type;
        return type is not null and not IErrorTypeSymbol
            && bodies.All(b => SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(b, cancellationToken).Type, type));
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static string BuildChain(List<string> conditions, List<string> statements, string indent, string unit, string eol, bool braces, bool allman)
    {
        var chain = new StringBuilder();
        for (var i = 0; i < statements.Count; i++)
        {
            var head = i == statements.Count - 1 ? "else" : (i == 0 ? "if (" : "else if (") + conditions[i] + ")";
            var body = indent + unit + statements[i];

            if (i > 0)
            {
                chain.Append(braces && !allman ? " " : eol + indent);
            }

            if (!braces)
            {
                chain.Append(head).Append(eol).Append(body);
            }
            else if (allman)
            {
                chain.Append(head).Append(eol).Append(indent).Append('{').Append(eol).Append(body).Append(eol).Append(indent).Append('}');
            }
            else
            {
                chain.Append(head).Append(" {").Append(eol).Append(body).Append(eol).Append(indent).Append('}');
            }
        }

        return chain.ToString();
    }

    // No braces only when the file already leaves single-statement 'if' / 'else' bodies without them more often than with them.
    private static bool ExistingStyleUsesBraces(SyntaxNode root)
    {
        var bodies = new List<StatementSyntax>();
        foreach (var ifStatement in root.DescendantNodes().OfType<IfStatementSyntax>())
        {
            bodies.Add(ifStatement.Statement);
            if (ifStatement.Else is { Statement: not IfStatementSyntax } elseClause)
            {
                bodies.Add(elseClause.Statement);
            }
        }

        return bodies.Count(s => s is BlockSyntax) >= bodies.Count(s => s is not BlockSyntax);
    }

    private static bool IsAllman(StatementSyntax statement) =>
        statement.FirstAncestorOrSelf<BlockSyntax>() is not { } block
        || block.OpenBraceToken.LeadingTrivia.Any(SyntaxKind.EndOfLineTrivia)
        || block.OpenBraceToken.GetPreviousToken().TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia);

    private static string IndentUnit(SyntaxNode parent, string indent, SourceText text)
    {
        if (parent is BlockSyntax block && LineIndent(block.OpenBraceToken.SpanStart, text) is { } braceIndent
            && indent.Length > braceIndent.Length && indent.StartsWith(braceIndent, StringComparison.Ordinal))
        {
            return indent.Substring(braceIndent.Length);
        }

        return indent.Contains('\t') ? "\t" : "    ";
    }

    /// <summary>The whitespace before <paramref name="position"/> on its line, or <c>null</c> when something else precedes it.</summary>
    private static string? LineIndent(int position, SourceText text)
    {
        var before = text.ToString(TextSpan.FromBounds(text.Lines.GetLineFromPosition(position).Start, position));
        return before.All(c => c is ' ' or '\t') ? before : null;
    }
}
