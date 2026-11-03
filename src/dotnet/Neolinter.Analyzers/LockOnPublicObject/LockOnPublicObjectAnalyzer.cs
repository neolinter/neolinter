using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.LockOnPublicObject;

/// <summary>NLR0031: locking on an object that any other code can also lock on (<c>this</c>, a string, a type, a boxed value).</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LockOnPublicObjectAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0031";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Lock on a publicly visible object",
        messageFormat: "Locking on {0} is risky: other code can lock on the same object and cause a deadlock or no mutual exclusion; lock on a private readonly object instead",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Lock on a private object that only the owning class can reach: 'this', strings, types and boxed values are shared with unrelated code.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0031.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.LockStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var expression = ((LockStatementSyntax)context.Node).Expression;
        var target = expression;
        while (target is ParenthesizedExpressionSyntax parenthesized)
        {
            target = parenthesized.Expression;
        }

        var what = Describe(target, context);
        if (what is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, expression.GetLocation(), what));
        }
    }

    private static string? Describe(ExpressionSyntax target, SyntaxNodeAnalysisContext context)
    {
        var model = context.SemanticModel;
        switch (target)
        {
            case ThisExpressionSyntax:
                return IsHidden(context.ContainingSymbol?.ContainingType) ? null : "'this'";
            case TypeOfExpressionSyntax:
                return "a type object";
            case CastExpressionSyntax cast
                when model.GetTypeInfo(cast.Expression, context.CancellationToken).Type is { IsValueType: true }:
                return "a boxed value";
        }

        var type = model.GetTypeInfo(target, context.CancellationToken).Type;
        if (type is null)
        {
            return null;
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            return "a string";
        }

        var typeClass = model.Compilation.GetTypeByMetadataName("System.Type");
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, typeClass))
            {
                return "a type object";
            }
        }

        return null;
    }

    // 'this' is only reachable by outside code when the type can be derived from or named from outside.
    private static bool IsHidden(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        if (type.IsSealed)
        {
            return true;
        }

        for (var t = type; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility == Accessibility.Private)
            {
                return true;
            }
        }

        return false;
    }
}
