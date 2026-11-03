using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.AsyncVoid;

/// <summary>NLR0024: an <c>async</c> method or lambda returning <c>void</c> hides its exceptions from the caller.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncVoidAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0024";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Avoid async void outside event handlers",
        messageFormat: "Avoid 'async void': return Task (Func<Task> for a lambda) so the caller can await it and observe its exceptions",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An exception thrown by an async void method cannot be caught by the caller and can take the process down; it cannot be awaited either.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0024.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocalFunction, SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var node = (MethodDeclarationSyntax)context.Node;
        if (!node.Modifiers.Any(SyntaxKind.AsyncKeyword)
            || context.SemanticModel.GetDeclaredSymbol(node, context.CancellationToken) is not IMethodSymbol { ReturnsVoid: true } method
            || IsEventHandlerShape(method.Parameters)
            || method.IsOverride
            || ImplementsInterfaceMember(method)
            || IsUsedAsDelegate(method, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, node.Identifier.GetLocation()));
    }

    private static void AnalyzeLocalFunction(SyntaxNodeAnalysisContext context)
    {
        var node = (LocalFunctionStatementSyntax)context.Node;
        if (!node.Modifiers.Any(SyntaxKind.AsyncKeyword)
            || context.SemanticModel.GetDeclaredSymbol(node, context.CancellationToken) is not IMethodSymbol { ReturnsVoid: true } method
            || IsEventHandlerShape(method.Parameters)
            || IsUsedAsDelegate(method, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, node.Identifier.GetLocation()));
    }

    private static void AnalyzeLambda(SyntaxNodeAnalysisContext context)
    {
        var node = (AnonymousFunctionExpressionSyntax)context.Node;
        if (node.AsyncKeyword.IsKind(SyntaxKind.None)
            || context.SemanticModel.GetTypeInfo(node, context.CancellationToken).ConvertedType is not INamedTypeSymbol { DelegateInvokeMethod: { ReturnsVoid: true } invoke }
            || IsEventHandlerShape(invoke.Parameters)
            || IsEventSubscription(node, context.SemanticModel, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, node.AsyncKeyword.GetLocation()));
    }

    /// <summary>The classic <c>(object sender, SomeEventArgs e)</c> signature.</summary>
    private static bool IsEventHandlerShape(ImmutableArray<IParameterSymbol> parameters)
    {
        if (parameters.Length != 2 || parameters[0].Type.SpecialType != SpecialType.System_Object)
        {
            return false;
        }

        for (var type = parameters[1].Type; type is not null; type = type.BaseType)
        {
            if (type is { Name: "EventArgs", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A lambda added to an event keeps its <c>void</c> signature, whatever the delegate type is.</summary>
    private static bool IsEventSubscription(SyntaxNode lambda, SemanticModel model, CancellationToken token)
    {
        var parent = lambda.Parent;
        while (parent is ParenthesizedExpressionSyntax)
        {
            parent = parent.Parent;
        }

        return parent is AssignmentExpressionSyntax assignment
            && assignment.IsKind(SyntaxKind.AddAssignmentExpression)
            && model.GetSymbolInfo(assignment.Left, token).Symbol is IEventSymbol;
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol method)
    {
        if (!method.ExplicitInterfaceImplementations.IsDefaultOrEmpty)
        {
            return true;
        }

        return method.ContainingType.AllInterfaces
            .SelectMany(i => i.GetMembers(method.Name))
            .Any(member => SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method));
    }

    /// <summary>
    /// True when the method is named without being called (method group), which converts it to a delegate such as
    /// <c>Action</c> or an event handler: its signature cannot change. Only reached for the rare async void method, so
    /// the whole compilation is scanned instead of being tracked. Analyzers must not create semantic models for other
    /// trees, so a matching name there is taken as a use without checking what it binds to.
    /// </summary>
    private static bool IsUsedAsDelegate(IMethodSymbol method, SemanticModel model, CancellationToken token)
    {
        foreach (var tree in model.Compilation.SyntaxTrees)
        {
            foreach (var name in tree.GetRoot(token).DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (name.Identifier.ValueText != method.Name || IsInvoked(name))
                {
                    continue;
                }

                if (tree != model.SyntaxTree)
                {
                    return true;
                }

                var info = model.GetSymbolInfo(name, token);
                if (info.Symbol is IMethodSymbol m && SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, method.OriginalDefinition)
                    || info.CandidateSymbols.OfType<IMethodSymbol>().Any(c => SymbolEqualityComparer.Default.Equals(c.OriginalDefinition, method.OriginalDefinition)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsInvoked(IdentifierNameSyntax name)
    {
        SyntaxNode current = name;
        if (current.Parent is MemberAccessExpressionSyntax access && access.Name == name)
        {
            current = access;
        }
        else if (current.Parent is MemberBindingExpressionSyntax binding && binding.Name == name)
        {
            current = binding;
        }

        return current.Parent is InvocationExpressionSyntax invocation && invocation.Expression == current;
    }
}
