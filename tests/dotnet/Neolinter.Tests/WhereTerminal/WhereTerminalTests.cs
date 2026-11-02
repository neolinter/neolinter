using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.WhereTerminal;
using Neolinter.CodeFixes.WhereTerminal;

namespace Neolinter.Tests.WhereTerminal;

/// <summary>NLR0012 tests. Several diagnostics in one source also exercise fix all.</summary>
public class WhereTerminalTests
{
    private sealed class Test : CSharpCodeFixTest<WhereTerminalAnalyzer, WhereTerminalCodeFixProvider, DefaultVerifier>
    {
        public Test(string source, string fixedSource)
        {
            TestCode = source;
            FixedCode = fixedSource;
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        }
    }

    private static Task FixAsync(string source, string fixedSource) => new Test(source, fixedSource).RunAsync();

    private static Task NoDiagnosticAsync(string source) => new Test(source, source).RunAsync();

    private static string Method(string body) =>
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;

        class OwnFirst : List<int>
        {
            public int First() => 0;
        }

        class OwnWhere
        {
            public IEnumerable<int> Where(Func<int, bool> p) => new int[0];
        }

        class C
        {
            static bool IsEven(int x) => x % 2 == 0;

            object M(IEnumerable<int> xs, List<int> list, int[] array, IQueryable<int> q, OwnFirst ownFirst, OwnWhere ownWhere)
            {
        {{body}}    }
        }
        """;

    [Theory]
    [InlineData("Any", "Any")]
    [InlineData("First", "First")]
    [InlineData("FirstOrDefault", "FirstOrDefault")]
    [InlineData("Single", "Single")]
    [InlineData("SingleOrDefault", "SingleOrDefault")]
    [InlineData("Last", "Last")]
    [InlineData("LastOrDefault", "LastOrDefault")]
    [InlineData("Count", "Count")]
    [InlineData("LongCount", "LongCount")]
    public Task EveryTerminalOperator(string terminal, string expected) => FixAsync(
        Method($"        return xs.{{|NLR0012:Where|}}(x => x > 1).{terminal}();\n"),
        Method($"        return xs.{expected}(x => x > 1);\n"));

    [Theory]
    [InlineData("list.{|NLR0012:Where|}(x => x > 1).Any()", "list.Any(x => x > 1)")]
    [InlineData("array.{|NLR0012:Where|}(x => x > 1).First()", "array.First(x => x > 1)")]
    [InlineData("q.{|NLR0012:Where|}(x => x > 1).Any()", "q.Any(x => x > 1)")]
    [InlineData("q.{|NLR0012:Where|}(x => x > 1).SingleOrDefault()", "q.SingleOrDefault(x => x > 1)")]
    [InlineData("xs.{|NLR0012:Where|}(IsEven).Any()", "xs.Any(IsEven)")]
    [InlineData("xs.{|NLR0012:Where<int>|}(x => x > 1).Count()", "xs.Count(x => x > 1)")]
    [InlineData("xs.{|NLR0012:Where|}(x => x > 1).Count<int>()", "xs.Count<int>(x => x > 1)")]
    [InlineData("xs.Where(x => x > 1).{|NLR0012:Where|}(x => x < 9).Any()", "xs.Where(x => x > 1).Any(x => x < 9)")]
    [InlineData("xs.Select(x => x + 1).{|NLR0012:Where|}(x => x > 1).Last()", "xs.Select(x => x + 1).Last(x => x > 1)")]
    [InlineData("(xs.{|NLR0012:Where|}(x => x > 1).Any())", "(xs.Any(x => x > 1))")]
    public Task Reported(string expression, string expected) =>
        FixAsync(Method($"        return {expression};\n"), Method($"        return {expected};\n"));

    [Fact]
    public Task KeepsLayoutAndCommentsAround() => FixAsync(
        Method("        return /* a */ xs\n            .{|NLR0012:Where|}(x => x > 1)\n            .Any(); // b\n"),
        Method("        return /* a */ xs\n            .Any(x => x > 1); // b\n"));

    [Fact]
    public Task FixAllSeveralInOneSource() => FixAsync(
        Method("        var a = xs.{|NLR0012:Where|}(x => x > 1).Any();\n        var b = list.{|NLR0012:Where|}(x => x > 1).FirstOrDefault();\n        return a ? b : q.{|NLR0012:Where|}(x => x > 1).Count();\n"),
        Method("        var a = xs.Any(x => x > 1);\n        var b = list.FirstOrDefault(x => x > 1);\n        return a ? b : q.Count(x => x > 1);\n"));

    // --- Together with NLR0011 -----------------------------------------------

    [Fact]
    public Task CountComparedWithZeroIsLeftToNlr0011() => NoDiagnosticAsync(
        Method("        return xs.Where(x => x > 1).Count() > 0;\n"));

    [Fact]
    public Task CountComparedWithOtherNumberIsFolded() => FixAsync(
        Method("        return xs.{|NLR0012:Where|}(x => x > 1).Count() > 1;\n"),
        Method("        return xs.Count(x => x > 1) > 1;\n"));

    // --- Not reported ------------------------------------------------------

    [Theory]
    [InlineData("xs.Where((x, i) => i > 1).Any()")]
    [InlineData("xs.Where(x => x > 1).First(x => x < 9)")]
    [InlineData("xs.Where(x => x > 1).ToList()")]
    [InlineData("xs.Where(x => x > 1).Select(x => x).Any()")]
    [InlineData("xs.Where(x => x > 1).ElementAt(0)")]
    [InlineData("xs.Where(x => x > 1).Max()")]
    [InlineData("xs.Where(x => x > 1)")]
    [InlineData("ownFirst.Where(x => x > 1).First()")]
    [InlineData("ownWhere.Where(x => x > 1).Any()")]
    [InlineData("Enumerable.Any(xs.Where(x => x > 1))")]
    [InlineData("xs?.Where(x => x > 1).Any()")]
    [InlineData("xs.Where(x => x > 1)/* c */.Any()")]
    [InlineData("xs.Where(x => x > 1).Any/* c */()")]
    public Task NotReported(string expression) => NoDiagnosticAsync(Method($"        return {expression};\n"));
}
