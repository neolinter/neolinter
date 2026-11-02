using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Neolinter.Analyzers.ForeachWhere;

internal sealed class ForeachWhereCandidate(ForEachStatementSyntax loop, ImmutableArray<IfStatementSyntax> guards, string variableName, bool needsUsing)
{
    public ForEachStatementSyntax Loop { get; } = loop;

    /// <summary>The leading <c>if (...) continue;</c> statements the fix moves into the source, in order.</summary>
    public ImmutableArray<IfStatementSyntax> Guards { get; } = guards;

    public string VariableName { get; } = variableName;

    /// <summary><c>System.Linq</c> is referenced but not imported at the loop.</summary>
    public bool NeedsUsing { get; } = needsUsing;
}

/// <summary>Shared by the NLR0017 analyzer and its code fix: leading <c>continue</c> guards of a <c>foreach</c> become <c>Where</c>.</summary>
internal static class ForeachWhereAnalysis
{
    public static ForeachWhereCandidate? Analyze(IfStatementSyntax first, SemanticModel model, CancellationToken cancellationToken)
    {
        if (first.Parent is not BlockSyntax { Parent: ForEachStatementSyntax { AwaitKeyword.RawKind: 0 } loop } block
            || block.Statements[0] != first
            || loop.Expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax
                or ThisExpressionSyntax or ParenthesizedExpressionSyntax or ObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax)
            || model.GetDeclaredSymbol(loop, cancellationToken) is not { RefKind: RefKind.None } variable
            || variable.Type.IsRefLikeType
            || model.GetForEachStatementInfo(loop).ElementType?.NullableAnnotation == NullableAnnotation.Annotated
            || variable.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Dynamic
            || !IsGenericEnumerable(model.GetTypeInfo(loop.Expression, cancellationToken).Type, variable.Type)
            || (!loop.Type.IsVar && !SymbolEqualityComparer.Default.Equals(model.GetForEachStatementInfo(loop).ElementType, variable.Type))
            || EnclosingTypeIsStruct(loop, model, cancellationToken))
        {
            return null;
        }

        var enumerable = model.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        if (enumerable is null || HasOwnWhere(model, loop, cancellationToken))
        {
            return null;
        }

        // Longest run of qualifying guards; the first one must qualify, and something must remain in the body.
        var guards = ImmutableArray.CreateBuilder<IfStatementSyntax>();
        foreach (var statement in block.Statements)
        {
            if (statement is not IfStatementSyntax candidate || !IsGuard(candidate) || !IsPure(candidate.Condition, variable, loop, model, cancellationToken))
            {
                break;
            }

            guards.Add(candidate);
        }

        if (guards.Count == 0 || guards.Count == block.Statements.Count)
        {
            return null;
        }

        var used = guards.SelectMany(g => g.Condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            .Select(n => model.GetSymbolInfo(n, cancellationToken).Symbol)
            .Where(s => s is not null && !SymbolEqualityComparer.Default.Equals(s, variable))
            .ToList();
        if (AssignsAny(block, used, model, cancellationToken))
        {
            return null;
        }

        var imported = !model.LookupNamespacesAndTypes(loop.SpanStart, name: "Enumerable").IsEmpty;

        // The fix adds 'using System.Linq;'; a file without usings but with a header comment or directive is left alone.
        if (!imported && loop.SyntaxTree.GetRoot(cancellationToken) is CompilationUnitSyntax { Usings.Count: 0 } unit
            && unit.GetFirstToken().LeadingTrivia.Any(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia)))
        {
            return null;
        }

        return new ForeachWhereCandidate(loop, guards.ToImmutable(), variable.Name, !imported);
    }

    // 'if (c) continue;' or 'if (c) { continue; }', no else, and no comment or directive that would be lost with the statement.
    private static bool IsGuard(IfStatementSyntax statement) =>
        statement.Else is null
        && (statement.Statement is ContinueStatementSyntax
            || (statement.Statement is BlockSyntax { Statements.Count: 1 } single && single.Statements[0] is ContinueStatementSyntax))
        && !statement.DescendantTrivia(descendIntoTrivia: true).Concat(statement.GetLeadingTrivia()).Concat(statement.GetTrailingTrivia())
            .Any(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia));

    private static bool IsGenericEnumerable(ITypeSymbol? source, ITypeSymbol element)
    {
        if (source is null)
        {
            return false;
        }

        var all = source.AllInterfaces.ToList();
        if (source is INamedTypeSymbol { TypeKind: TypeKind.Interface } self)
        {
            all.Add(self);
        }

        // An IQueryable source would turn the guard into part of the query, which is another meaning.
        return all.Any(i => i.Name == "IQueryable") == false
            && all.Any(i => i is { Name: "IEnumerable", TypeArguments.Length: 1 } && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], element));
    }

    private static bool EnclosingTypeIsStruct(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetEnclosingSymbol(node.SpanStart, cancellationToken)?.ContainingType is { TypeKind: TypeKind.Struct };

    // An instance member called Where on the source type would win over the extension method.
    private static bool HasOwnWhere(SemanticModel model, ForEachStatementSyntax loop, CancellationToken cancellationToken) =>
        model.GetTypeInfo(loop.Expression, cancellationToken).Type is { } type
        && !model.LookupSymbols(loop.Expression.SpanStart, type, "Where", includeReducedExtensionMethods: false).IsEmpty;

    private static bool IsPure(ExpressionSyntax condition, ILocalSymbol variable, ForEachStatementSyntax loop, SemanticModel model, CancellationToken cancellationToken)
    {
        foreach (var node in condition.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case IdentifierNameSyntax name:
                    if (!IsReadable(model.GetSymbolInfo(name, cancellationToken).Symbol, variable, IsCalled(name)))
                    {
                        return false;
                    }

                    break;

                case TypeSyntax:
                case LiteralExpressionSyntax:
                case ParenthesizedExpressionSyntax:
                case ElementAccessExpressionSyntax:
                case BracketedArgumentListSyntax:
                case ArgumentListSyntax:
                case ConditionalExpressionSyntax:
                case ConditionalAccessExpressionSyntax:
                case MemberBindingExpressionSyntax:
                case ElementBindingExpressionSyntax:
                case CastExpressionSyntax:
                case TypeOfExpressionSyntax:
                case DefaultExpressionSyntax:
                case IsPatternExpressionSyntax:
                case ConstantPatternSyntax:
                case TypePatternSyntax:
                case RelationalPatternSyntax:
                case UnaryPatternSyntax:
                case BinaryPatternSyntax:
                case ParenthesizedPatternSyntax:
                case RecursivePatternSyntax:
                case PropertyPatternClauseSyntax:
                case SubpatternSyntax:
                case DiscardPatternSyntax:
                case DiscardDesignationSyntax:
                case ThisExpressionSyntax:
                    break;

                case ArgumentSyntax { RefKindKeyword.RawKind: 0, NameColon: null }:
                    break;

                case PrefixUnaryExpressionSyntax prefix when prefix.Kind() is SyntaxKind.LogicalNotExpression or SyntaxKind.UnaryMinusExpression
                    or SyntaxKind.UnaryPlusExpression or SyntaxKind.BitwiseNotExpression:
                    break;

                case BinaryExpressionSyntax:
                    break;

                case MemberAccessExpressionSyntax access when access.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                    break;

                case InvocationExpressionSyntax invocation:
                    if (!IsPureCall(invocation, model, cancellationToken))
                    {
                        return false;
                    }

                    break;

                default:
                    return false;
            }

            if (node is ExpressionSyntax expression && model.GetTypeInfo(expression, cancellationToken).Type is { } type
                && (type.IsRefLikeType || type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer))
            {
                return false;
            }
        }

        return true;
    }

    // Symbols a lambda can read: the loop variable, other locals and parameters passed by value, fields, properties, constants and types.
    private static bool IsReadable(ISymbol? symbol, ILocalSymbol variable, bool called) => symbol switch
    {
        null => false,
        ILocalSymbol local => SymbolEqualityComparer.Default.Equals(local, variable) || local.RefKind == RefKind.None,
        IParameterSymbol parameter => parameter.RefKind == RefKind.None,
        IFieldSymbol or IPropertySymbol or INamespaceOrTypeSymbol => true,
        IMethodSymbol => called,
        _ => false,
    };

    // The name of a method only counts when it is called right there, not when it becomes a delegate.
    private static bool IsCalled(IdentifierNameSyntax name) =>
        name.Parent switch
        {
            InvocationExpressionSyntax invocation => invocation.Expression == name,
            MemberAccessExpressionSyntax access => access.Name == name && access.Parent is InvocationExpressionSyntax call && call.Expression == access,
            _ => false,
        };

    // 'nameof(...)', and methods of 'string' and the primitive types ('s.StartsWith("a")', 'string.IsNullOrEmpty(s)').
    private static bool IsPureCall(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetConstantValue(invocation, cancellationToken).HasValue)
        {
            return true;
        }

        return model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol { ReturnsVoid: false, RefKind: RefKind.None } method
            && method.Parameters.All(p => p.RefKind == RefKind.None)
            && method.ContainingType.SpecialType is SpecialType.System_String or SpecialType.System_Boolean or SpecialType.System_Char
                or SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
    }

    // The body must not write to anything the condition reads, directly or through ref, out or in.
    private static bool AssignsAny(BlockSyntax body, List<ISymbol?> used, SemanticModel model, CancellationToken cancellationToken)
    {
        if (used.Count == 0)
        {
            return false;
        }

        bool Hits(ExpressionSyntax target) =>
            target.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                .Any(n => model.GetSymbolInfo(n, cancellationToken).Symbol is { } s && used.Any(u => SymbolEqualityComparer.Default.Equals(u, s)));

        foreach (var node in body.DescendantNodes())
        {
            var written = node switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left,
                PrefixUnaryExpressionSyntax prefix when prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression => prefix.Operand,
                PostfixUnaryExpressionSyntax postfix when postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression => postfix.Operand,
                ArgumentSyntax { RefKindKeyword.RawKind: not 0 } argument => argument.Expression,
                _ => null,
            };

            if (written is not null && Hits(written))
            {
                return true;
            }
        }

        return false;
    }
}
