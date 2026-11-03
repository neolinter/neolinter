using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Neolinter.Analyzers.MultipleEnumeration;

/// <summary>Detection shared by the NLR0032 analyzer and its code fix.</summary>
internal static class MultipleEnumerationAnalysis
{
    // LINQ operators that only build a new deferred sequence: the source is not read until the result is consumed.
    private static readonly HashSet<string> Deferred =
    [
        "Where", "Select", "SelectMany", "Skip", "SkipWhile", "SkipLast", "Take", "TakeWhile", "TakeLast", "OrderBy", "OrderByDescending",
        "ThenBy", "ThenByDescending", "Order", "OrderDescending", "Reverse", "Distinct", "DistinctBy", "Concat", "Union", "UnionBy",
        "Intersect", "IntersectBy", "Except", "ExceptBy", "Zip", "Cast", "OfType", "Append", "Prepend", "GroupBy", "Join", "GroupJoin",
        "Chunk", "AsEnumerable", "AsQueryable", "DefaultIfEmpty",
    ];

    /// <summary>IEnumerable, IEnumerable&lt;T&gt;, IQueryable and IQueryable&lt;T&gt; themselves (not List, arrays or other collections).</summary>
    public static bool IsDeferredType(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Interface } named
        && (named.OriginalDefinition.SpecialType is SpecialType.System_Collections_IEnumerable or SpecialType.System_Collections_Generic_IEnumerable_T
            || named.OriginalDefinition is { Name: "IQueryable", ContainingNamespace: { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } } });

    /// <summary>The nodes of a body, without lambdas and local functions (which are analyzed on their own, or ignored).</summary>
    public static IEnumerable<SyntaxNode> OwnNodes(SyntaxNode body) =>
        body.DescendantNodesAndSelf(n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));

    /// <summary>An assignment, a ref or out argument, or a type test: the variable is not simply "the same sequence", so it is left alone.</summary>
    public static bool IsWriteOrTypeTest(IdentifierNameSyntax identifier)
    {
        SyntaxNode node = identifier;
        while (node.Parent is ParenthesizedExpressionSyntax)
        {
            node = node.Parent;
        }

        return node.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == node,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
            BinaryExpressionSyntax binary => binary.IsKind(SyntaxKind.IsExpression) || binary.IsKind(SyntaxKind.AsExpression),
            IsPatternExpressionSyntax => true,
            SwitchStatementSyntax or SwitchExpressionSyntax => true,
            _ => false,
        };
    }

    /// <summary>True when the identifier is the source of a foreach or of a consuming LINQ call (through any number of deferred operators).</summary>
    public static bool IsEnumeration(IdentifierNameSyntax identifier, SemanticModel model, CancellationToken token)
    {
        SyntaxNode node = identifier;
        while (true)
        {
            while (node.Parent is ParenthesizedExpressionSyntax)
            {
                node = node.Parent;
            }

            if (node.Parent is ForEachStatementSyntax forEach)
            {
                return forEach.Expression == node;
            }

            if (node.Parent is not MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax invocation } access
                || access.Expression != node
                || invocation.Expression != access
                || model.GetSymbolInfo(invocation, token).Symbol is not IMethodSymbol method)
            {
                return false;
            }

            if (method.Name == "GetEnumerator")
            {
                return true;
            }

            if (method.ContainingType is not { Name: "Enumerable" or "Queryable", ContainingNamespace: { Name: "Linq", ContainingNamespace.Name: "System" } })
            {
                return false;
            }

            if (!Deferred.Contains(method.Name))
            {
                return true;
            }

            node = invocation;
        }
    }

    /// <summary>True when a loop around the use runs it again and the variable is not declared inside that loop.</summary>
    public static bool IsRepeatedByLoop(SyntaxNode use, ISymbol symbol, SyntaxNode body)
    {
        var declaration = symbol is ILocalSymbol ? symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() : null;
        for (var current = use; current is not null && current != body; current = current.Parent)
        {
            var parent = current.Parent;
            var repeated = parent switch
            {
                ForEachStatementSyntax f => f.Statement == current,
                WhileStatementSyntax or DoStatementSyntax => true,
                ForStatementSyntax f => f.Statement == current || f.Condition == current || (current is ExpressionSyntax e && f.Incrementors.Contains(e)),
                _ => false,
            };
            if (repeated && (declaration is null || !parent!.Span.Contains(declaration.Span)))
            {
                return true;
            }
        }

        return false;
    }

    public static ControlFlowGraph? CreateGraph(SemanticModel model, SyntaxNode method, CancellationToken token)
    {
        try
        {
            return (model.GetOperation(method, token)) switch
            {
                ILocalFunctionOperation { Body: { } block } => ControlFlowGraph.Create(block, cancellationToken: token),
                IMethodBodyOperation body => ControlFlowGraph.Create(body, cancellationToken: token),
                IConstructorBodyOperation constructor => ControlFlowGraph.Create(constructor, cancellationToken: token),
                _ => null,
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static Dictionary<SyntaxNode, BasicBlock> MapBlocks(ControlFlowGraph graph)
    {
        var map = new Dictionary<SyntaxNode, BasicBlock>();
        foreach (var block in graph.Blocks)
        {
            var roots = block.BranchValue is { } value ? block.Operations.Concat(new[] { value }) : block.Operations;
            foreach (var root in roots)
            {
                foreach (var operation in root.DescendantsAndSelf())
                {
                    if (operation is ILocalReferenceOperation or IParameterReferenceOperation)
                    {
                        map[operation.Syntax] = block;
                    }
                }
            }
        }

        return map;
    }

    /// <summary>True when the second enumeration can run after the first one, in the flow graph.</summary>
    public static bool Reaches(SyntaxNode first, SyntaxNode second, Dictionary<SyntaxNode, BasicBlock> blocks)
    {
        if (!blocks.TryGetValue(first, out var from) || !blocks.TryGetValue(second, out var to))
        {
            return false;
        }

        if (from == to)
        {
            return true;
        }

        var seen = new HashSet<int> { from.Ordinal };
        var queue = new Queue<BasicBlock>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var block = queue.Dequeue();
            foreach (var next in new[] { block.FallThroughSuccessor?.Destination, block.ConditionalSuccessor?.Destination })
            {
                if (next is null || !seen.Add(next.Ordinal))
                {
                    continue;
                }

                if (next == to)
                {
                    return true;
                }

                queue.Enqueue(next);
            }
        }

        return false;
    }
}
