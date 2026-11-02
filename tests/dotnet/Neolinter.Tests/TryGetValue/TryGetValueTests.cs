using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.TryGetValue;
using Neolinter.CodeFixes.TryGetValue;

namespace Neolinter.Tests.TryGetValue;

/// <summary>NLR0015 tests. Several diagnostics in one source also exercise fix all.</summary>
public class TryGetValueTests
{
    private static Task Run(string source, string fixedSource, LanguageVersion languageVersion, string? batchFixedSource = null, int? iterations = null)
    {
        var test = new CSharpCodeFixTest<TryGetValueAnalyzer, TryGetValueCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        if (batchFixedSource is not null)
        {
            test.BatchFixedCode = batchFixedSource;
        }

        if (iterations is { } n)
        {
            test.NumberOfIncrementalIterations = n;
            test.NumberOfFixAllIterations = n;
        }

        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var options = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
            return solution.WithProjectParseOptions(projectId, options.WithLanguageVersion(languageVersion));
        });

        return test.RunAsync();
    }

    private static Task FixAsync(string source, string fixedSource, LanguageVersion languageVersion = LanguageVersion.Latest) =>
        Run(source, fixedSource, languageVersion);

    private static Task NoDiagnosticAsync(string source, LanguageVersion languageVersion = LanguageVersion.Latest) =>
        Run(source, source, languageVersion);

    private static string Method(string body) =>
        $$"""
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using System.Threading.Tasks;

        class C
        {
            Dictionary<string, int> _map = new Dictionary<string, int>();
            Dictionary<string, int> Map { get; } = new Dictionary<string, int>();

            int M(Dictionary<string, int> d, Dictionary<string, int> e, IDictionary<string, int> id, IReadOnlyDictionary<string, int> ro,
                ConcurrentDictionary<string, int> cd, string k, string other, IQueryable<string> q)
            {
        {{body}}    }
        }
        """;

    // --- Reported ------------------------------------------------------------

    [Fact]
    public Task IfThen() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|})\n        {\n            return d[k];\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value))\n        {\n            return value;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task IfThenSeveralReads() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|})\n        {\n            var x = d[k] + 1;\n            return x * d[k];\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value))\n        {\n            var x = value + 1;\n            return x * value;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task IfWithoutBraces() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|})\n            return d[k];\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value))\n            return value;\n\n        return 0;\n"));

    [Fact]
    public Task NegatedWithElse() => FixAsync(
        Method("        if (!{|NLR0015:d.ContainsKey(k)|})\n        {\n            return 0;\n        }\n        else\n        {\n            return d[k];\n        }\n"),
        Method("        if (!d.TryGetValue(k, out var value))\n        {\n            return 0;\n        }\n        else\n        {\n            return value;\n        }\n"));

    [Fact]
    public Task AndInIf() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|} && d[k] > 0)\n        {\n            return 1;\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value) && value > 0)\n        {\n            return 1;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task AndChainInIf() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|} && other != null && d[k] > 0)\n        {\n            return 1;\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value) && other != null && value > 0)\n        {\n            return 1;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task AndInReturn() => FixAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            bool M(Dictionary<string, int> d, string k)
            {
                return {|NLR0015:d.ContainsKey(k)|} && d[k] > 0;
            }

            bool N(Dictionary<string, int> d, string k) => {|NLR0015:d.ContainsKey(k)|} && d[k] > 0;
        }
        """,
        $$"""
        using System.Collections.Generic;

        class C
        {
            bool M(Dictionary<string, int> d, string k)
            {
                return d.TryGetValue(k, out var value) && value > 0;
            }

            bool N(Dictionary<string, int> d, string k) => d.TryGetValue(k, out var value) && value > 0;
        }
        """);

    [Fact]
    public Task AndInLambda() => FixAsync(
        Method("        Func<string, bool> f = x => {|NLR0015:d.ContainsKey(x)|} && d[x] > 0;\n        return f(k) ? 1 : 0;\n"),
        Method("        Func<string, bool> f = x => d.TryGetValue(x, out var value) && value > 0;\n        return f(k) ? 1 : 0;\n"));

    [Theory]
    [InlineData("id")]
    [InlineData("ro")]
    [InlineData("cd")]
    [InlineData("_map")]
    [InlineData("this.Map")]
    public Task Receivers(string receiver) => FixAsync(
        Method($"        if ({{|NLR0015:{receiver}.ContainsKey(k)|}})\n        {{\n            return {receiver}[k];\n        }}\n\n        return 0;\n"),
        Method($"        if ({receiver}.TryGetValue(k, out var value))\n        {{\n            return value;\n        }}\n\n        return 0;\n"));

    [Fact]
    public Task ConstantKey() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(\"a\")|})\n        {\n            return d[\"a\"];\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(\"a\", out var value))\n        {\n            return value;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task NameTaken_UsesNextName() => FixAsync(
        Method("        var value = 3;\n        if ({|NLR0015:d.ContainsKey(k)|})\n        {\n            return d[k] + value;\n        }\n\n        return 0;\n"),
        Method("        var value = 3;\n        if (d.TryGetValue(k, out var value1))\n        {\n            return value1 + value;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task NameTakenLater_UsesNextName() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|})\n        {\n            return d[k];\n        }\n\n        var value = 3;\n        return value;\n"),
        Method("        if (d.TryGetValue(k, out var value1))\n        {\n            return value1;\n        }\n\n        var value = 3;\n        return value;\n"));

    [Fact]
    public Task KeepsCommentsAndLayout() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey( /* a */ k)|}) // b\n        {\n            // c\n            return /* d */ d[k]; // e\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue( /* a */ k, out var value)) // b\n        {\n            // c\n            return /* d */ value; // e\n        }\n\n        return 0;\n"));

    [Fact]
    public Task ReadsAfterOtherCallsOnTheDictionary_AreFine() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|})\n        {\n            Console.WriteLine(d.Count);\n            return d[k];\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value))\n        {\n            Console.WriteLine(d.Count);\n            return value;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task MutationAfterTheLastRead_IsFine() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|})\n        {\n            var x = d[k];\n            d.Remove(k);\n            return x;\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value))\n        {\n            var x = value;\n            d.Remove(k);\n            return x;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task SeveralInOneSource_FixAll() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|})\n        {\n            return d[k];\n        }\n\n        if ({|NLR0015:e.ContainsKey(other)|} && e[other] > 1)\n        {\n            return 2;\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value))\n        {\n            return value;\n        }\n\n        if (e.TryGetValue(other, out var value1) && value1 > 1)\n        {\n            return 2;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task CSharp7() => FixAsync(
        Method("        if ({|NLR0015:d.ContainsKey(k)|})\n        {\n            return d[k];\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value))\n        {\n            return value;\n        }\n\n        return 0;\n"),
        LanguageVersion.CSharp7);

    // --- Not reported --------------------------------------------------------

    [Fact]
    public Task CSharp6_NoDiagnostic() => NoDiagnosticAsync(
        Method("        if (d.ContainsKey(k))\n        {\n            return d[k];\n        }\n\n        return 0;\n"),
        LanguageVersion.CSharp6);

    [Theory]
    [InlineData("if (d.ContainsKey(k)) { return d[k]; } else { return 0; }")]
    [InlineData("if (!d.ContainsKey(k)) { return d[k]; } return 0;")]
    [InlineData("if (!d.ContainsKey(k)) { return 0; } return d[k];")]
    [InlineData("if (d.ContainsKey(k)) { return 1; } return d[k];")]
    [InlineData("if (d.ContainsKey(k)) { return d[other]; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { return e[k]; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { return d[\"a\"]; } return 0;")]
    [InlineData("if (d.ContainsKey(k) || d[k] > 0) { return 1; } return 0;")]
    [InlineData("if (other != null && d.ContainsKey(k) && d[k] > 0) { return 1; } return 0;")]
    [InlineData("return d.ContainsKey(k) && d[k] > 0 ? 1 : 0;")]
    [InlineData("if (d.ContainsKey(k) && d.Count > 0) { return 1; } return 0;")]
    [InlineData("var b = d.ContainsKey(k) && d[k] > 0; return b ? 1 : 0;")]
    [InlineData("if (d.ContainsKey(k.Trim())) { return d[k.Trim()]; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { d[k] = 1; return 1; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { d[k] += 1; return 1; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { d[k]++; return 1; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { d.Remove(k); return d[k]; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { d.Clear(); return d[k]; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { d[other] = 2; return d[k]; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { Console.WriteLine(d); return d[k]; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { d = e; return d[k]; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { k = other; return d[k]; } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { Func<int> f = () => d[k]; return f(); } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { int Local() => d[k]; return Local(); } return 0;")]
    [InlineData("if (d.ContainsKey(k)) { return d[/* why */ k]; } return 0;")]
    public Task NotReported(string code) => NoDiagnosticAsync(
        Method("        " + code + "\n").Replace("class C\n{", "class C\n{\n    static void Foo(ref int x) { }\n    static void Reset(Dictionary<string, int> x) { }\n"));

    [Fact]
    public Task AwaitBeforeTheRead_NoDiagnostic() => NoDiagnosticAsync(
        """
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            async Task<int> M(Dictionary<string, int> d, string k)
            {
                if (d.ContainsKey(k))
                {
                    await Task.Delay(1);
                    return d[k];
                }

                return 0;
            }
        }
        """);

    [Fact]
    public Task NestedCheck_InnerIsReportedFirst() => Run(
        Method("        if (d.ContainsKey(k))\n        {\n            if ({|NLR0015:e.ContainsKey(k)|})\n            {\n                return e[k] + d[k];\n            }\n        }\n\n        return 0;\n"),
        Method("        if (d.TryGetValue(k, out var value1))\n        {\n            if (e.TryGetValue(k, out var value))\n            {\n                return value + value1;\n            }\n        }\n\n        return 0;\n"),
        LanguageVersion.Latest,
        iterations: 2);

    [Fact]
    public Task QueryAndExpressionTree_NoDiagnostic() => NoDiagnosticAsync(
        Method("        var r = from x in q where d.ContainsKey(x) && d[x] > 0 select x;\n        Expression<Func<string, bool>> f = x => d.ContainsKey(x) && d[x] > 0;\n        return r.Count();\n"));

    [Fact]
    public Task TypeWithoutTryGetValue_NoDiagnostic() => NoDiagnosticAsync(
        """
        class Bag
        {
            public bool ContainsKey(string key) => true;

            public int this[string key] => 1;
        }

        class C
        {
            int M(Bag bag, string k)
            {
                if (bag.ContainsKey(k))
                {
                    return bag[k];
                }

                return 0;
            }
        }
        """);

    [Fact]
    public Task ExtensionTryGetValue_NoDiagnostic() => NoDiagnosticAsync(
        """
        class Bag
        {
            public bool ContainsKey(string key) => true;

            public int this[string key] => 1;
        }

        static class Extensions
        {
            public static bool TryGetValue(this Bag bag, string key, out int value)
            {
                value = 1;
                return true;
            }
        }

        class C
        {
            int M(Bag bag, string k)
            {
                if (bag.ContainsKey(k))
                {
                    return bag[k];
                }

                return 0;
            }
        }
        """);
}
