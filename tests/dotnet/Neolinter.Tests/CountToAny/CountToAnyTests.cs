using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.CountToAny;
using Neolinter.CodeFixes.CountToAny;

namespace Neolinter.Tests.CountToAny;

/// <summary>NLR0011 tests. Several diagnostics in one source also exercise fix all.</summary>
public class CountToAnyTests
{
    private sealed class Test : CSharpCodeFixTest<CountToAnyAnalyzer, CountToAnyCodeFixProvider, DefaultVerifier>
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
        using System.Collections.Generic;
        using System.Linq;

        class Own
        {
            public int Count() => 0;
        }

        class WithAny : List<int>
        {
            public bool Any => true;
        }

        class C
        {
            bool M(IEnumerable<int> xs, List<int> list, int[] array, IQueryable<int> q, Own own, WithAny withAny)
            {
        {{body}}    }
        }
        """;

    [Theory]
    [InlineData("xs.Count() > 0", "xs.Any()")]
    [InlineData("xs.Count() != 0", "xs.Any()")]
    [InlineData("xs.Count() >= 1", "xs.Any()")]
    [InlineData("0 < xs.Count()", "xs.Any()")]
    [InlineData("0 != xs.Count()", "xs.Any()")]
    [InlineData("1 <= xs.Count()", "xs.Any()")]
    [InlineData("xs.Count() == 0", "!xs.Any()")]
    [InlineData("xs.Count() < 1", "!xs.Any()")]
    [InlineData("0 == xs.Count()", "!xs.Any()")]
    [InlineData("1 > xs.Count()", "!xs.Any()")]
    [InlineData("xs.Count(x => x > 1) > 0", "xs.Any(x => x > 1)")]
    [InlineData("xs.Count(x => x > 1) == 0", "!xs.Any(x => x > 1)")]
    [InlineData("list.Count() > 0", "list.Any()")]
    [InlineData("array.Count() > 0", "array.Any()")]
    [InlineData("q.Count() > 0", "q.Any()")]
    [InlineData("q.Count(x => x > 1) == 0", "!q.Any(x => x > 1)")]
    [InlineData("xs.Where(x => x > 1).Count() > 0", "xs.Where(x => x > 1).Any()")]
    [InlineData("xs.Count<int>() > 0", "xs.Any<int>()")]
    [InlineData("(xs.Count() > 0)", "(xs.Any())")]
    public Task Reported(string condition, string expected)
    {
        var reported = condition.StartsWith('(') ? "({|NLR0011:" + condition[1..^1] + "|})" : "{|NLR0011:" + condition + "|}";
        return FixAsync(Method($"        return {reported};\n"), Method($"        return {expected};\n"));
    }

    [Fact]
    public Task NegationInsideALargerCondition() => FixAsync(
        Method("        return list.Count > 3 && {|NLR0011:xs.Count() == 0|};\n"),
        Method("        return list.Count > 3 && !xs.Any();\n"));

    [Fact]
    public Task ReceiverIsAChain() => FixAsync(
        Method("        return {|NLR0011:list.Select(x => x + 1).ToList().Count() > 0|};\n"),
        Method("        return list.Select(x => x + 1).ToList().Any();\n"));

    [Fact]
    public Task KeepsLayoutAndCommentsAround() => FixAsync(
        Method("        return /* a */ {|NLR0011:xs\n            .Count() > 0|}; // b\n"),
        Method("        return /* a */ xs\n            .Any(); // b\n"));

    [Fact]
    public Task FixAllSeveralInOneSource() => FixAsync(
        Method("        var a = {|NLR0011:xs.Count() > 0|};\n        var b = {|NLR0011:0 == list.Count()|};\n        return a && {|NLR0011:q.Count(x => x > 1) != 0|} || b;\n"),
        Method("        var a = xs.Any();\n        var b = !list.Any();\n        return a && q.Any(x => x > 1) || b;\n"));

    // --- Not reported ------------------------------------------------------

    [Theory]
    [InlineData("list.Count > 0")]
    [InlineData("array.Length > 0")]
    [InlineData("own.Count() > 0")]
    [InlineData("withAny.Count() > 0")]
    [InlineData("xs.Count() > 1")]
    [InlineData("xs.Count() == 1")]
    [InlineData("xs.Count() >= 0")]
    [InlineData("xs.Count() <= 0")]
    [InlineData("xs.Count() > 0L")]
    [InlineData("xs.Count() > list.Count")]
    [InlineData("xs.LongCount() > 0")]
    [InlineData("Enumerable.Count(xs) > 0")]
    [InlineData("(xs?.Count() > 0) == true")]
    [InlineData("xs.Count() > /* none */ 0")]
    [InlineData("xs.Any()")]
    public Task NotReported(string condition) => NoDiagnosticAsync(Method($"        return {condition};\n"));
}
