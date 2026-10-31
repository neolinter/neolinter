using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.InvertIf;
using Neolinter.CodeFixes.InvertIf;

namespace Neolinter.Tests;

/// <summary>Runs NL0001 analyzer and code fix tests. Mark expected diagnostics with <c>{|NL0001:if|}</c>.</summary>
internal static class Verify
{
    public static Task NoDiagnosticAsync(string source, LanguageVersion languageVersion = LanguageVersion.Latest, string? editorConfig = null) =>
        FixAsync(source, source, languageVersion, editorConfig);

    public static Task FixAsync(string source, string fixedSource, LanguageVersion languageVersion = LanguageVersion.Latest, string? editorConfig = null)
    {
        var test = new CSharpCodeFixTest<InvertIfAnalyzer, InvertIfCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        if (editorConfig is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n\n[*]\n" + editorConfig + "\n"));
        }

        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var options = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
            return solution.WithProjectParseOptions(projectId, options.WithLanguageVersion(languageVersion));
        });

        return test.RunAsync();
    }
}
