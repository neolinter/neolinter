using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.ForeachWhere;
using Neolinter.CodeFixes.ForeachWhere;

namespace Neolinter.Tests.ForeachWhere;

/// <summary>NLR0018 tests. Several diagnostics in one source also exercise fix all.</summary>
public class ForeachTakeWhileTests
{
    private static Task Run(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<ForeachTakeWhileAnalyzer, ForeachTakeWhileCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var options = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
            return solution.WithProjectParseOptions(projectId, options.WithLanguageVersion(LanguageVersion.Latest));
        });

        return test.RunAsync();
    }

    private static Task FixAsync(string source, string fixedSource) => Run(source, fixedSource);

    private static Task NoDiagnosticAsync(string source) => Run(source, source);

    private static string Class(string members, string usings = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n") =>
        usings + "\nclass Item { public bool Active; public string Name; public int Age; }\n\nclass C\n{\n    int _limit;\n\n    void A() { }\n    void B() { }\n\n" + members + "}\n";

    private static string Method(string loop, string parameters = "List<Item> items") =>
        $"    void M({parameters}, int n)\n    {{\n{loop}    }}\n";

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task NegatedGuard() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0018:if (!x.Active) break;|}\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.TakeWhile(x => x.Active))\n        {\n            A();\n        }\n")));

    [Fact]
    public Task GuardIsNegatedInTheFix() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0018:if (x.Age > _limit) break;|}\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.TakeWhile(x => x.Age <= _limit))\n        {\n            A();\n        }\n")));

    [Fact]
    public Task BracedGuardAndBlankLine() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0018:if (!x.Active)\n            {\n                break;\n            }|}\n\n            A();\n            B();\n        }\n")),
        Class(Method("        foreach (var x in items.TakeWhile(x => x.Active))\n        {\n            A();\n            B();\n        }\n")));

    [Fact]
    public Task SeveralGuardsAreMerged() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0018:if (!x.Active) break;|}\n            if (x.Age > 3 || x.Age < 1) break;\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.TakeWhile(x => x.Active && !(x.Age > 3 || x.Age < 1)))\n        {\n            A();\n        }\n")));

    // 'if (a) break; if (b) continue;' is one TakeWhile and a remaining 'continue' guard (NLR0017 then handles that one).
    [Fact]
    public Task ABreakRunFollowedByAContinueGuard() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0018:if (!x.Active) break;|}\n            if (x.Age > 3) continue;\n            A();\n        }\n")),
        Class(Method("        foreach (var x in items.TakeWhile(x => x.Active))\n        {\n            if (x.Age > 3) continue;\n            A();\n        }\n")));

    [Fact]
    public Task OnlyTheLeadingRunIsMoved() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0018:if (!x.Active) break;|}\n            A();\n            if (x.Age > 3) break;\n            B();\n        }\n")),
        Class(Method("        foreach (var x in items.TakeWhile(x => x.Active))\n        {\n            A();\n            if (x.Age > 3) break;\n            B();\n        }\n")));

    [Fact]
    public Task NestedLoopsFixAll() => FixAsync(
        Class(Method("        foreach (var x in items)\n        {\n            {|NLR0018:if (!x.Active) break;|}\n            foreach (var y in items)\n            {\n                {|NLR0018:if (y.Age < x.Age) break;|}\n                A();\n            }\n        }\n")),
        Class(Method("        foreach (var x in items.TakeWhile(x => x.Active))\n        {\n            foreach (var y in items.TakeWhile(y => y.Age >= x.Age))\n            {\n                A();\n            }\n        }\n")));

    [Fact]
    public Task AddsTheUsingToAFileWithoutUsings() => FixAsync(
        "class Item { public bool Active; }\n\nclass C\n{\n    void M(Item[] items)\n    {\n        foreach (var x in items)\n        {\n            {|NLR0018:if (!x.Active) break;|}\n            M(items);\n        }\n    }\n}\n",
        "using System.Linq;\n\nclass Item { public bool Active; }\n\nclass C\n{\n    void M(Item[] items)\n    {\n        foreach (var x in items.TakeWhile(x => x.Active))\n        {\n            M(items);\n        }\n    }\n}\n");

    [Fact]
    public Task StringElementsAndExplicitType() => FixAsync(
        Class(Method("        foreach (Item x in items)\n        {\n            {|NLR0018:if (!x.Active) break;|}\n            A();\n        }\n\n        foreach (var c in text)\n        {\n            {|NLR0018:if (c == '.') break;|}\n            A();\n        }\n", "Item[] items, string text")),
        Class(Method("        foreach (Item x in items.TakeWhile(x => x.Active))\n        {\n            A();\n        }\n\n        foreach (var c in text.TakeWhile(c => c != '.'))\n        {\n            A();\n        }\n", "Item[] items, string text")));

    // --- Not reported ------------------------------------------------------------------

    [Theory]
    [InlineData("if (!x.Active) { break; } else { B(); }")]
    [InlineData("if (!x.Active) { A(); break; }")]
    [InlineData("if (!x.Active) continue;")]
    [InlineData("if (!x.Active) return;")]
    [InlineData("A(); if (!x.Active) break;")]
    [InlineData("// stop\n            if (!x.Active) break;")]
    [InlineData("if (!x.Active) break; // stop")]
    // A 'continue' guard before a 'break' guard: the order of the two filters matters, no single rewrite is safe.
    [InlineData("if (x.Age > 3) continue; if (!x.Active) break;")]
    public Task Shapes(string statements) => NoDiagnosticAsync(
        Class(Method($"        foreach (var x in items)\n        {{\n            {statements}\n            B();\n        }}\n")));

    [Fact]
    public Task GuardsAreTheWholeBody() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            if (!x.Active) break;\n        }\n")));

    [Fact]
    public Task BreakOfAnotherLoop() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            for (var i = 0; i < n; i++)\n            {\n                if (i == 1) break;\n                A();\n            }\n        }\n")));

    [Theory]
    [InlineData("foreach (var x in items.AsQueryable())", "x.Active")]
    [InlineData("foreach (object x in items)", "x == null")]
    [InlineData("foreach (var x in items?.Select(i => i))", "x.Active")]
    public Task Loops(string header, string condition) => NoDiagnosticAsync(
        Class(Method($"        {header}\n        {{\n            if ({condition}) break;\n            A();\n        }}\n")));

    [Fact]
    public Task AwaitForeach() => NoDiagnosticAsync(
        Class("    async System.Threading.Tasks.Task M(IAsyncEnumerable<Item> items)\n    {\n        await foreach (var x in items)\n        {\n            if (!x.Active) break;\n            A();\n        }\n    }\n"));

    [Theory]
    [InlineData("x.Active && Check(x)")]
    [InlineData("!items.Any(i => i == x)")]
    [InlineData("(n = 1) > 0")]
    [InlineData("int.TryParse(x.Name, out var v)")]
    public Task ImpureOrUnsupportedConditions(string condition) => NoDiagnosticAsync(
        Class("    static bool Check(Item i) => true;\n" + Method($"        foreach (var x in items)\n        {{\n            if ({condition}) break;\n            A();\n        }}\n")));

    [Fact]
    public Task BodyAssignsWhatTheConditionReads() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            if (n > x.Age) break;\n            n = x.Age;\n        }\n")));

    [Fact]
    public Task NullableElements() => NoDiagnosticAsync(
        "#nullable enable\nusing System.Linq;\n\nclass C\n{\n    void M(string?[] items)\n    {\n        foreach (var s in items)\n        {\n            if (s == null) break;\n            _ = s.Length;\n        }\n    }\n}\n");

    [Fact]
    public Task OwnTakeWhileOnTheSource() => NoDiagnosticAsync(
        Class(Method("        foreach (var x in items)\n        {\n            if (!x.Active) break;\n            A();\n        }\n", "Own items") + "    class Own : List<Item> { public IEnumerable<Item> TakeWhile(Func<Item, bool> p) => this; }\n"));

    [Fact]
    public Task StructMethod() => NoDiagnosticAsync(
        "using System.Collections.Generic;\nusing System.Linq;\n\nstruct S\n{\n    int _f;\n\n    void M(List<int> items)\n    {\n        foreach (var x in items)\n        {\n            if (x > _f) break;\n            M(items);\n        }\n    }\n}\n");

    [Fact]
    public Task NoUsingsAndAHeaderComment() => NoDiagnosticAsync(
        "// Header.\nclass Item { public bool Active; }\n\nclass C\n{\n    void M(Item[] items)\n    {\n        foreach (var x in items)\n        {\n            if (!x.Active) break;\n            M(items);\n        }\n    }\n}\n");

    // --- Meaning --------------------------------------------------------------------

    // The rewrite must keep the visited items and the number of predicate calls and source reads (the source is read lazily).
    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 3)]
    [InlineData(new[] { 9, 9 }, 3)]
    [InlineData(new int[0], 3)]
    [InlineData(new[] { 1, 2 }, 3)]
    public void TakeWhileKeepsTheSemantics(int[] data, int limit)
    {
        var (visitedA, callsA, readsA) = Loop(data, limit, rewritten: false);
        var (visitedB, callsB, readsB) = Loop(data, limit, rewritten: true);

        Assert.Equal(visitedA, visitedB);
        Assert.Equal(callsA, callsB);
        Assert.Equal(readsA, readsB);
    }

    private static (List<int> Visited, int Calls, int Reads) Loop(int[] data, int limit, bool rewritten)
    {
        var visited = new List<int>();
        var calls = 0;
        var reads = 0;
        bool Keep(int v) { calls++; return v < limit; }
        IEnumerable<int> Source() { foreach (var v in data) { reads++; yield return v; } }

        if (rewritten)
        {
            foreach (var x in Source().TakeWhile(x => Keep(x)))
            {
                visited.Add(x);
            }
        }
        else
        {
            foreach (var x in Source())
            {
                if (!Keep(x)) break;
                visited.Add(x);
            }
        }

        return (visited, calls, reads);
    }
}
