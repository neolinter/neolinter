using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.ForeachWhere;
using Neolinter.CodeFixes.ForeachWhere;

namespace Neolinter.Tests.ForeachWhere;

/// <summary>NLR0017 tests. Several diagnostics in one source also exercise fix all.</summary>
public class ForeachWhereTests
{
    private static Task Run(string source, string fixedSource, LanguageVersion languageVersion)
    {
        var test = new CSharpCodeFixTest<ForeachWhereAnalyzer, ForeachWhereCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
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

    private static string Class(string members, string usings = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n") =>
        usings + "\nclass Item { public bool Active; public string Name; public int Age; }\n\nclass C\n{\n    int _limit;\n    const int Max = 3;\n\n    void A() { }\n    void B() { }\n\n" + members + "}\n";

    private static string Method(string loop, string parameters = "List<Item> items") =>
        $"    void M({parameters}, int n)\n    {{\n{loop}    }}\n";

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task NegatedGuard() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (!x.Active) continue;|}\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => x.Active))\n        {\n            A();\n        }\n")));

    [Fact]
    public Task GuardIsNegatedInTheFix() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (x.Name == null) continue;|}\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => x.Name != null))\n        {\n            A();\n        }\n")));

    [Fact]
    public Task BracedGuardAndBlankLine() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (!x.Active)\n            {\n                continue;\n            }|}\n\n            A();\n            B();\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => x.Active))\n        {\n            A();\n            B();\n        }\n")));

    [Fact]
    public Task SeveralGuardsAreMerged() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (!x.Active) continue;|}\n            if (x.Age > 3 || x.Age < 1) continue;\n            if (!(x.Name != null && n > 0)) continue;\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => x.Active && !(x.Age > 3 || x.Age < 1) && x.Name != null && n > 0))\n        {\n            A();\n        }\n")));

    [Fact]
    public Task OnlyTheLeadingRunIsMoved() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (!x.Active) continue;|}\n            A();\n            if (x.Age > 3) continue;\n            B();\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => x.Active))\n        {\n            A();\n            if (x.Age > 3) continue;\n            B();\n        }\n")));

    [Fact]
    public Task CommentOnTheFirstRemainingStatementStays() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (!x.Active) continue;|}\n\n            // Do the work.\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => x.Active))\n        {\n            // Do the work.\n            A();\n        }\n")));

    [Fact]
    public Task ArrayStringAndExplicitType() => FixAsync(
        Class(Method("        foreach (Item x in items)\n        {\n            {|NLR0017:if (!x.Active) continue;|}\n            A();\n        }\n\n        foreach (var c in text)\n        {\n            {|NLR0017:if (c == 'a') continue;|}\n            A();\n        }\n", "Item[] items, string text")),
        Class(Method("        foreach (Item x in items.Where(x => x.Active))\n        {\n            A();\n        }\n\n        foreach (var c in text.Where(c => c != 'a'))\n        {\n            A();\n        }\n", "Item[] items, string text")));

    [Fact]
    public Task ConditionWithFieldsConstantsPatternsAndStringCalls() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (x.Age > _limit || x.Age == Max || x.Name is null or \"\" || x.Name.StartsWith(\"a\") || string.IsNullOrEmpty(x.Name)) continue;|}\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => !(x.Age > _limit || x.Age == Max || x.Name is null or \"\" || x.Name.StartsWith(\"a\") || string.IsNullOrEmpty(x.Name))))\n        {\n            A();\n        }\n")));

    [Fact]
    public Task ConditionOnSeveralLines() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (!(x.Active\n                && x.Age > 3)) continue;|}\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => x.Active\n                && x.Age > 3))\n        {\n            A();\n        }\n")));

    [Fact]
    public Task SourceIsAMemberAccess() => FixAsync(
        Class(Method("        foreach (var x in h.Items)\n        {\n            {|NLR0017:if (!x.Active) continue;|}\n            A();\n        }\n", "Holder h") + "    class Holder { public List<Item> Items; }\n"),
        Class(Method("        foreach (var x in h.Items.Where(x => x.Active))\n        {\n            A();\n        }\n", "Holder h") + "    class Holder { public List<Item> Items; }\n"));

    [Fact]
    public Task NestedLoopsFixAll() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (!x.Active) continue;|}\n            foreach (var y in items)\n            {\n                {|NLR0017:if (y.Age < x.Age) continue;|}\n                A();\n            }\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => x.Active))\n        {\n            foreach (var y in items.Where(y => y.Age >= x.Age))\n            {\n                A();\n            }\n        }\n")));

    [Fact]
    public Task AddsTheUsingAfterTheLast() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (!x.Active) continue;|}\n            A();\n        }\n\n        foreach (var y in items)\n        {\n            {|NLR0017:if (!y.Active) continue;|}\n            B();\n        }\n"), "using System;\nusing System.Collections.Generic;\n"),
        Class(Method("        foreach (var x in items.Where(x => x.Active))\n        {\n            A();\n        }\n\n        foreach (var y in items.Where(y => y.Active))\n        {\n            B();\n        }\n"), "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n"));

    [Fact]
    public Task AddsTheUsingToAFileWithoutUsings() => FixAsync(
        "class Item { public bool Active; }\n\nclass C\n{\n    void M(Item[] items)\n    {\n        foreach (var x in items)\n        {\n            {|NLR0017:if (!x.Active) continue;|}\n            M(items);\n        }\n    }\n}\n",
        "using System.Linq;\n\nclass Item { public bool Active; }\n\nclass C\n{\n    void M(Item[] items)\n    {\n        foreach (var x in items.Where(x => x.Active))\n        {\n            M(items);\n        }\n    }\n}\n");

    // NLR0001 turns 'if (c) { ... }' into this shape; this rule then moves the guard.
    [Fact]
    public Task AfterNLR0001() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0017:if (!x.Active)\n            {\n                continue;\n            }|}\n\n            A();\n            A();\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.Where(x => x.Active))\n        {\n            A();\n            A();\n            A();\n        }\n")));

    [Fact]
    public Task StringElementsWithNullableEnabled() => FixAsync(
        """
        #nullable enable
        using System.Collections.Generic;
        using System.Linq;

        class C
        {
            int M(List<string> names)
            {
                var total = 0;
                foreach (var name in names)
                {
                    {|NLR0017:if (!name.StartsWith("a")) continue;|}
                    total += name.Length;
                }

                return total;
            }
        }
        """,
        """
        #nullable enable
        using System.Collections.Generic;
        using System.Linq;

        class C
        {
            int M(List<string> names)
            {
                var total = 0;
                foreach (var name in names.Where(name => name.StartsWith("a")))
                {
                    total += name.Length;
                }

                return total;
            }
        }
        """);

    // --- Not reported ------------------------------------------------------------------

    [Theory]
    [InlineData("if (!x.Active) { continue; } else { B(); }")]
    [InlineData("if (!x.Active) { A(); continue; }")]
    [InlineData("if (!x.Active) break;")]
    [InlineData("if (!x.Active) return;")]
    [InlineData("A(); if (!x.Active) continue;")]
    [InlineData("// skipped\n            if (!x.Active) continue;")]
    [InlineData("if (!x.Active) continue; // skipped")]
    [InlineData("if (!x.Active) /* skipped */ continue;")]
    public Task Shapes(string statements) => NoDiagnosticAsync(
        Class(Method($"        foreach (var x in items)\n        {{\n            {statements}\n            B();\n        }}\n")));

    [Fact]
    public Task GuardIsTheLastStatement() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            if (!x.Active) continue;\n        }\n")));

    [Fact]
    public Task GuardsAreTheWholeBody() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            if (!x.Active) continue;\n            if (x.Age > 3) continue;\n        }\n")));

    [Fact]
    public Task ContinueOfAnotherLoop() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            for (var i = 0; i < n; i++)\n            {\n                if (i == 1) continue;\n                A();\n            }\n        }\n")));

    [Fact]
    public Task WithoutBlock() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n            if (!x.Active) continue;\n            else A();\n")));

    [Theory]
    [InlineData("foreach (var (a, b) in pairs)", "a == 1")]
    [InlineData("foreach (var x in items.AsQueryable())", "x.Active")]
    [InlineData("foreach (object x in items)", "x == null")]
    [InlineData("foreach (var x in items?.Select(i => i))", "x.Active")]
    public Task Loops(string header, string condition) => NoDiagnosticAsync(
        Class(Method($"        {header}\n        {{\n            if ({condition}) continue;\n            A();\n        }}\n", "List<Item> items, (int, int)[] pairs")));

    [Fact]
    public Task AwaitForeach() => NoDiagnosticAsync(
        Class("    async System.Threading.Tasks.Task M(IAsyncEnumerable<Item> items)\n    {\n        await foreach (var x in items)\n        {\n            if (!x.Active) continue;\n            A();\n        }\n    }\n"));

    [Fact]
    public Task MultiDimensionalAndNonGenericSources() => NoDiagnosticAsync(
        Class("    void M(int[,] grid, System.Collections.ArrayList list)\n    {\n        foreach (var g in grid)\n        {\n            if (g == 0) continue;\n            A();\n        }\n\n        foreach (var o in list)\n        {\n            if (o == null) continue;\n            A();\n        }\n    }\n"));

    [Theory]
    [InlineData("x.Active && Check(x)")]
    [InlineData("!items.Any(i => i == x)")]
    [InlineData("(n = 1) > 0")]
    [InlineData("n++ > 0")]
    [InlineData("new Item() == x")]
    [InlineData("x.Name is string s && s.Length > 1")]
    [InlineData("int.TryParse(x.Name, out var v)")]
    [InlineData("int.TryParse(x.Name, out n)")]
    [InlineData("Local(x)")]
    public Task ImpureOrUnsupportedConditions(string condition) => NoDiagnosticAsync(
        Class("    static bool Check(Item i) => true;\n" + Method($"        bool Local(Item i) => true;\n        foreach (var x in items)\n        {{\n            if ({condition}) continue;\n            A();\n        }}\n")));

    [Fact]
    public Task BodyAssignsWhatTheConditionReads() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            if (n > x.Age) continue;\n            n = x.Age;\n        }\n")));

    [Fact]
    public Task BodyAssignsAFieldTheConditionReads() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            if (x.Age > _limit) continue;\n            _limit++;\n        }\n")));

    [Fact]
    public Task RefParameterAndRefStruct() => NoDiagnosticAsync(
        Class("    void M(ref int n, Span<int> span, List<Item> items)\n    {\n        foreach (var x in items)\n        {\n            if (x.Age > n) continue;\n            A();\n        }\n\n        foreach (var x in items)\n        {\n            if (x.Age > span.Length) continue;\n            A();\n        }\n    }\n"));

    [Fact]
    public Task NullableElements() => NoDiagnosticAsync(
        "#nullable enable\nusing System.Linq;\n\nclass C\n{\n    void M(string?[] items)\n    {\n        foreach (var s in items)\n        {\n            if (s == null) continue;\n            _ = s.Length;\n        }\n    }\n}\n");

    [Fact]
    public Task OwnWhereOnTheSource() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            if (!x.Active) continue;\n            A();\n        }\n", "Own items") + "    class Own : List<Item> { public IEnumerable<Item> Where(Func<Item, bool> p) => this; }\n"));

    [Fact]
    public Task StructMethod() => NoDiagnosticAsync(
        "using System.Collections.Generic;\nusing System.Linq;\n\nstruct S\n{\n    int _f;\n\n    void M(List<int> items)\n    {\n        foreach (var x in items)\n        {\n            if (x > _f) continue;\n            M(items);\n        }\n    }\n}\n");

    [Fact]
    public Task NoUsingsAndAHeaderComment() => NoDiagnosticAsync(
        "// Header.\nclass Item { public bool Active; }\n\nclass C\n{\n    void M(Item[] items)\n    {\n        foreach (var x in items)\n        {\n            if (!x.Active) continue;\n            M(items);\n        }\n    }\n}\n");

    [Fact]
    public Task ElementTypeDiffersFromTheVariableType() => NoDiagnosticAsync(
        Class("    void M(List<int> items)\n    {\n        foreach (long x in items)\n        {\n            if (x > 3) continue;\n            A();\n        }\n    }\n"));
}
