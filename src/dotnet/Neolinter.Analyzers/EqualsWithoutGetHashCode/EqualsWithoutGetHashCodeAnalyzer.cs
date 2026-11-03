using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.EqualsWithoutGetHashCode;

/// <summary>NLR0033: a type that defines equality without <c>GetHashCode</c>, or <c>GetHashCode</c> without <c>Equals</c>.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EqualsWithoutGetHashCodeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0033";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Equals without GetHashCode",
        messageFormat: "Type '{0}' {1}",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Equal objects must return the same hash code: override Equals and GetHashCode together, or neither.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0033.yaml");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsRecord || type.IsAbstract || type.IsStatic
            || type.Locations.FirstOrDefault(l => l.IsInSource) is not { } location)
        {
            return;
        }

        var ownHash = type.GetMembers("GetHashCode").OfType<IMethodSymbol>().FirstOrDefault(IsHashOverride);
        var definesEquality = type.GetMembers().OfType<IMethodSymbol>().Any(m => IsEqualsOverride(m) || IsEqualityOperator(m))
            || ImplementsEquatable(type, context.Compilation);

        if (ownHash is null && definesEquality && !BaseOverrides(type, IsHashOverride))
        {
            Report(context, location, type, "defines equality but does not override GetHashCode: equal objects can get different hash codes and be lost in hash-based collections");
        }
        else if (ownHash is not null && !definesEquality && !BaseOverrides(type, IsEqualsOverride)
                 && ownHash.Locations.FirstOrDefault(l => l.IsInSource) is { } hashLocation)
        {
            Report(context, hashLocation, type, "overrides GetHashCode but not Equals: the hash code then says nothing about equality");
        }
    }

    private static void Report(SymbolAnalysisContext context, Location location, INamedTypeSymbol type, string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name, reason));

    private static bool IsHashOverride(IMethodSymbol m) => m is { Name: "GetHashCode", IsOverride: true, Parameters.Length: 0 };

    private static bool IsEqualsOverride(IMethodSymbol m) =>
        m is { Name: "Equals", IsOverride: true, Parameters.Length: 1 } && m.Parameters[0].Type.SpecialType == SpecialType.System_Object;

    private static bool IsEqualityOperator(IMethodSymbol m) => m.Name is "op_Equality" or "op_Inequality";

    // The user's base classes count: a base that already overrides the method covers the derived type.
    private static bool BaseOverrides(INamedTypeSymbol type, Func<IMethodSymbol, bool> isOverride)
    {
        for (var b = type.BaseType; b is not null && b.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType); b = b.BaseType)
        {
            if (b.GetMembers().OfType<IMethodSymbol>().Any(isOverride))
            {
                return true;
            }
        }

        return false;
    }

    // True when the type itself (not a base class) declares the method that implements IEquatable<T>.Equals.
    private static bool ImplementsEquatable(INamedTypeSymbol type, Compilation compilation)
    {
        var equatable = compilation.GetTypeByMetadataName("System.IEquatable`1");
        if (equatable is null)
        {
            return false;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, equatable)
                && iface.GetMembers("Equals").FirstOrDefault() is { } member
                && type.FindImplementationForInterfaceMember(member) is IMethodSymbol impl
                && SymbolEqualityComparer.Default.Equals(impl.ContainingType, type))
            {
                return true;
            }
        }

        return false;
    }
}
