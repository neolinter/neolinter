using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.InvertIf;
using Neolinter.Analyzers.MergeNestedIf;

namespace Neolinter.Tests.MergeNestedIf;

/// <summary>Runs NLR0001 and NLR0008 together on the same source, as an IDE does.</summary>
internal sealed class BothRules : CSharpAnalyzerTest<MergeNestedIfAnalyzer, DefaultVerifier>
{
    public static Task RunAsync(string source, string? editorConfig = null)
    {
        var test = new BothRules { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net80 };
        if (editorConfig is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n\n[*]\n" + editorConfig + "\n"));
        }

        return test.RunAsync();
    }

    protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
        base.GetDiagnosticAnalyzers().Append(new InvertIfAnalyzer());
}
