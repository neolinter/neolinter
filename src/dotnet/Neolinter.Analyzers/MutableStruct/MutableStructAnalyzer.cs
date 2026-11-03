using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.MutableStruct;

/// <summary>NLR0034: a struct with a writable instance field or a property with a <c>set</c> accessor.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutableStructAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NLR0034";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Mutable struct",
        messageFormat: "Struct '{0}' is mutable: {1}",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A struct is copied on assignment and when passed, so changes to a copy are silently lost: make it immutable.",
        helpLinkUri: "https://github.com/neolinter/neolinter/blob/main/rules/NLR0034.yaml");

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
        if (type.TypeKind != TypeKind.Struct || type.IsReadOnly || type.IsRecord
            || type.Locations.FirstOrDefault(l => l.IsInSource) is not { } location)
        {
            return;
        }

        // Implicit members (auto-property backing fields, field-like events) are judged through their property.
        foreach (var member in type.GetMembers().Where(m => !m.IsStatic && !m.IsImplicitlyDeclared))
        {
            var reason = member switch
            {
                IFieldSymbol { IsReadOnly: false, IsConst: false } f => $"field '{f.Name}' is not readonly",
                IPropertySymbol { SetMethod: { IsInitOnly: false } } p => $"property '{p.Name}' has a set accessor",
                _ => null,
            };

            if (reason is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name, reason));
                return;
            }
        }
    }
}
