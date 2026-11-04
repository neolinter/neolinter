using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Neolinter.Analyzers.RandomInLoop;

/// <summary>NLR0036: <c>new Random()</c> without a seed created in the body of a loop.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RandomInLoopAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0036";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "new Random() created in a loop",
        messageFormat: "'new Random()' is created on every iteration; instances created in quick succession can return the same sequence",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Random instances created without a seed can start from the same time-based seed when created in quick succession: create one instance outside the loop, or use Random.Shared.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0036.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze, OperationKind.ObjectCreation);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Arguments.Length != 0
            || creation.Type is not { Name: "Random", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } }
            || !RunsPerIteration(creation))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, creation.Syntax.GetLocation()));
    }

    private static bool RunsPerIteration(IOperation operation)
    {
        for (IOperation child = operation, parent = operation.Parent!; parent is not null; child = parent, parent = parent.Parent!)
        {
            switch (parent)
            {
                // A lambda or local function may not run once per iteration.
                case IAnonymousFunctionOperation or ILocalFunctionOperation:
                    return false;
                // The collection of a foreach and the initializer of a for run once.
                case IForEachLoopOperation forEach when forEach.Collection != child:
                    return true;
                case IForLoopOperation forLoop when !forLoop.Before.Contains(child):
                    return true;
                case IWhileLoopOperation:
                    return true;
            }
        }

        return false;
    }
}
