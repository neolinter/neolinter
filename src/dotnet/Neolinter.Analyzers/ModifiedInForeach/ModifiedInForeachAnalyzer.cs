using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Neolinter.Analyzers.ModifiedInForeach;

/// <summary>NLR0035: a <c>foreach</c> body that adds to or removes from the collection being enumerated.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ModifiedInForeachAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0035";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Collection modified during foreach",
        messageFormat: "'{0}' is modified while a foreach enumerates it; the next iteration throws InvalidOperationException",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Changing a collection inside the foreach that enumerates it invalidates the enumerator and throws at runtime: iterate over a copy, or collect the changes and apply them after the loop.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0035.yaml");

    // Mutators that bump the version of a BCL collection.
    private static readonly ImmutableHashSet<string> AddMethods = ImmutableHashSet.Create(
        "Add", "AddRange", "Insert", "InsertRange", "AddFirst", "AddLast", "AddBefore", "AddAfter", "Push", "Enqueue",
        "TryAdd", "UnionWith", "SymmetricExceptWith");

    private static readonly ImmutableHashSet<string> RemoveMethods = ImmutableHashSet.Create(
        "Remove", "RemoveAt", "RemoveAll", "RemoveRange", "RemoveFirst", "RemoveLast", "Pop", "TryPop", "Dequeue",
        "TryDequeue", "Clear", "ExceptWith", "IntersectWith", "Sort", "Reverse");

    // Enumerated types where removals do not invalidate the enumerator since .NET Core 3.0 (adds still do).
    private static readonly ImmutableHashSet<string> RemoveSafeTypes = ImmutableHashSet.Create(
        "System.Collections.Generic.Dictionary<TKey, TValue>",
        "System.Collections.Generic.HashSet<T>",
        "System.Collections.Generic.IDictionary<TKey, TValue>",
        "System.Collections.Generic.ISet<T>");

    private static readonly ImmutableHashSet<string> ThrowingTypes = RemoveSafeTypes.Union(new[]
    {
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.LinkedList<T>",
        "System.Collections.Generic.Queue<T>",
        "System.Collections.Generic.Stack<T>",
        "System.Collections.Generic.SortedSet<T>",
        "System.Collections.Generic.SortedDictionary<TKey, TValue>",
        "System.Collections.Generic.SortedList<TKey, TValue>",
        "System.Collections.ObjectModel.Collection<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.ICollection<T>",
    });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze, OperationKind.Loop);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        if (context.Operation is not IForEachLoopOperation loop || GetVariable(loop.Collection) is not { } variable
            || !TryGetKind(variable, out var removeSafe))
        {
            return;
        }

        foreach (var invocation in loop.Body.Descendants().OfType<IInvocationOperation>())
        {
            var name = invocation.TargetMethod.Name;
            if (invocation.TargetMethod.IsExtensionMethod
                || !(AddMethods.Contains(name) || (!removeSafe && RemoveMethods.Contains(name)))
                || !SymbolEqualityComparer.Default.Equals(GetVariable(invocation.Instance), variable)
                || !IsEnumerationLive(invocation, loop, variable))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), variable.Name));
        }
    }

    // A simple local, parameter or field of this (bare or through 'this'), seen through implicit conversions.
    private static ISymbol? GetVariable(IOperation? operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
        {
            operation = conversion.Operand;
        }

        return operation switch
        {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            IFieldReferenceOperation field when field.Instance is null or IInstanceReferenceOperation => field.Field,
            _ => null,
        };
    }

    private static bool TryGetKind(ISymbol variable, out bool removeSafe)
    {
        removeSafe = false;
        var type = variable switch
        {
            ILocalSymbol l => l.Type,
            IParameterSymbol p => p.Type,
            IFieldSymbol f => f.Type,
            _ => null,
        };

        for (; type is not null; type = type.BaseType)
        {
            var definition = type.OriginalDefinition.ToDisplayString();
            if (ThrowingTypes.Contains(definition))
            {
                removeSafe = RemoveSafeTypes.Contains(definition);
                return true;
            }
        }

        return false;
    }

    // False when the call sits in a lambda or local function, in an inner foreach over the same variable (that loop
    // reports it), or right before the enumerator is abandoned.
    private static bool IsEnumerationLive(IInvocationOperation invocation, IForEachLoopOperation loop, ISymbol variable)
    {
        for (var parent = invocation.Parent; parent is not null && parent != loop; parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation
                || (parent is IForEachLoopOperation inner && SymbolEqualityComparer.Default.Equals(GetVariable(inner.Collection), variable)))
            {
                return false;
            }
        }

        var statement = invocation.Syntax.FirstAncestorOrSelf<StatementSyntax>();
        if (statement is null)
        {
            return true;
        }

        // 'return coll.Remove(x);' and 'throw ...' never come back to MoveNext.
        if (statement is ReturnStatementSyntax or ThrowStatementSyntax)
        {
            return false;
        }

        if (statement is not ExpressionStatementSyntax || statement.Parent is not BlockSyntax block)
        {
            return true;
        }

        var next = block.Statements.ElementAtOrDefault(block.Statements.IndexOf(statement) + 1);
        return next switch
        {
            ReturnStatementSyntax or ThrowStatementSyntax or GotoStatementSyntax => false,
            YieldStatementSyntax y when y.IsKind(SyntaxKind.YieldBreakStatement) => false,
            BreakStatementSyntax b => !LeavesLoop(b, loop.Syntax),
            _ => true,
        };
    }

    // A 'break' leaves the foreach only when no closer loop or switch takes it.
    private static bool LeavesLoop(BreakStatementSyntax breakStatement, SyntaxNode loop) =>
        breakStatement.Ancestors().FirstOrDefault(a => a is ForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax
            or DoStatementSyntax or SwitchStatementSyntax or ForEachVariableStatementSyntax) == loop;
}
