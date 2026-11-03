using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Neolinter.Analyzers.MultipleEnumeration;

/// <summary>NLR0032: a sequence that may be a deferred query is enumerated more than once in the same method.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MultipleEnumerationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0032";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Multiple enumeration of an IEnumerable",
        messageFormat: "'{0}' may be enumerated more than once; if it is a deferred query it runs again each time",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Enumerating an IEnumerable or IQueryable twice runs a deferred query twice, with repeated work, repeated side effects or a different result; materialize it once with ToList() or ToArray().",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0032.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            Analyze,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.GetAccessorDeclaration,
            SyntaxKind.SetAccessorDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var model = context.SemanticModel;
        var token = context.CancellationToken;
        SyntaxNode? body;
        IEnumerable<ParameterSyntax> parameters = [];
        switch (context.Node)
        {
            case BaseMethodDeclarationSyntax method:
                body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
                parameters = method.ParameterList.Parameters;
                break;
            case LocalFunctionStatementSyntax function:
                body = (SyntaxNode?)function.Body ?? function.ExpressionBody;
                parameters = function.ParameterList.Parameters;
                break;
            case AccessorDeclarationSyntax accessor:
                body = (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody;
                break;
            default:
                return;
        }

        if (body is null)
        {
            return;
        }

        var tracked = new Dictionary<ISymbol, List<IdentifierNameSyntax>>(SymbolEqualityComparer.Default);
        foreach (var parameter in parameters)
        {
            if (model.GetDeclaredSymbol(parameter, token) is { RefKind: RefKind.None } p && MultipleEnumerationAnalysis.IsDeferredType(p.Type))
            {
                tracked[p] = [];
            }
        }

        var nodes = MultipleEnumerationAnalysis.OwnNodes(body).ToList();
        foreach (var declarator in nodes.OfType<VariableDeclaratorSyntax>())
        {
            if (declarator is { Initializer.Value: { } init, Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } }
                && model.GetDeclaredSymbol(declarator, token) is ILocalSymbol local
                && MultipleEnumerationAnalysis.IsDeferredType(local.Type)
                && MultipleEnumerationAnalysis.IsDeferredType(model.GetTypeInfo(init, token).Type))
            {
                tracked[local] = [];
            }
        }

        if (tracked.Count == 0)
        {
            return;
        }

        var excluded = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var identifier in nodes.OfType<IdentifierNameSyntax>())
        {
            if (model.GetSymbolInfo(identifier, token).Symbol is not { } symbol || !tracked.TryGetValue(symbol, out var uses))
            {
                continue;
            }

            if (MultipleEnumerationAnalysis.IsWriteOrTypeTest(identifier))
            {
                excluded.Add(symbol);
            }
            else if (MultipleEnumerationAnalysis.IsEnumeration(identifier, model, token))
            {
                uses.Add(identifier);
            }
        }

        Dictionary<SyntaxNode, BasicBlock>? blocks = null;
        foreach (var pair in tracked)
        {
            var symbol = pair.Key;
            var uses = pair.Value;
            if (excluded.Contains(symbol) || uses.Count == 0)
            {
                continue;
            }

            // One diagnostic per variable, on the first enumeration that is a repeat: in a loop, or reachable from an earlier one.
            var ordered = uses.OrderBy(u => u.SpanStart).ToList();
            var repeat = ordered.FirstOrDefault(u => MultipleEnumerationAnalysis.IsRepeatedByLoop(u, symbol, body));
            if (ordered.Count > 1)
            {
                blocks ??= MultipleEnumerationAnalysis.CreateGraph(model, context.Node, token) is { } graph
                    ? MultipleEnumerationAnalysis.MapBlocks(graph)
                    : [];
                for (var i = 1; i < ordered.Count && (repeat is null || ordered[i].SpanStart < repeat.SpanStart); i++)
                {
                    if (ordered.Take(i).Any(a => MultipleEnumerationAnalysis.Reaches(a, ordered[i], blocks)))
                    {
                        repeat = ordered[i];
                        break;
                    }
                }
            }

            if (repeat is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, repeat.GetLocation(), symbol.Name));
            }
        }
    }
}
