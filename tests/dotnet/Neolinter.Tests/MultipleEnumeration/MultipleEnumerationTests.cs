using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.MultipleEnumeration;
using Neolinter.CodeFixes.MultipleEnumeration;

namespace Neolinter.Tests.MultipleEnumeration;

/// <summary>NLR0032 tests. The fix only exists for locals; several diagnostics in one source also exercise fix all.</summary>
public class MultipleEnumerationTests
{
    private sealed class Test : CSharpCodeFixTest<MultipleEnumerationAnalyzer, MultipleEnumerationCodeFixProvider, DefaultVerifier>
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

    private static string Class(string body) =>
        "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n\nclass C\n{\n    static IEnumerable<int> Source() => new[] { 1, 2 };\n\n    void M(IEnumerable<int> items, List<int> list, int[] array, IQueryable<int> query, bool flag)\n    {\n" + body + "    }\n}\n";

    // --- Reported, parameters (no fix) ---------------------------------------------

    [Fact]
    public Task CountThenForeach() => NoDiagnosticOrFix(
        Class("        var n = items.Count();\n        foreach (var i in {|NLR0032:items|})\n        {\n        }\n"));

    [Fact]
    public Task AnyThenFirst() => NoDiagnosticOrFix(
        Class("        if (items.Any())\n        {\n            var f = {|NLR0032:items|}.First();\n        }\n"));

    [Fact]
    public Task ChainedOperatorsCountOnce() => NoDiagnosticOrFix(
        Class("        var a = items.Where(i => i > 0).Count();\n        var b = {|NLR0032:items|}.Select(i => i).ToList();\n"));

    [Fact]
    public Task QueryableParameter() => NoDiagnosticOrFix(
        Class("        var a = query.Count();\n        var b = {|NLR0032:query|}.ToList();\n"));

    [Fact]
    public Task GetEnumeratorAndForeachInsideChain() => NoDiagnosticOrFix(
        Class("        foreach (var i in items.Where(x => x > 0))\n        {\n        }\n\n        var c = {|NLR0032:items|}.Count();\n"));

    [Fact]
    public Task EnumerationInLoopBody() => NoDiagnosticOrFix(
        Class("        foreach (var i in list)\n        {\n            var c = {|NLR0032:items|}.Count();\n        }\n"));

    [Fact]
    public Task EnumerationInWhileCondition() => NoDiagnosticOrFix(
        Class("        while ({|NLR0032:items|}.Any())\n        {\n            break;\n        }\n"));

    [Fact]
    public Task EnumerationInForCondition() => NoDiagnosticOrFix(
        Class("        for (var i = 0; i < {|NLR0032:items|}.Count(); i++)\n        {\n        }\n"));

    [Fact]
    public Task ThenAfterBranchThatFallsThrough() => NoDiagnosticOrFix(
        Class("        if (flag)\n        {\n            var a = items.ToList();\n        }\n\n        var b = {|NLR0032:items|}.Count();\n"));

    [Fact]
    public Task InConstructor() => NoDiagnosticOrFix(
        "using System.Collections.Generic;\nusing System.Linq;\n\nclass C\n{\n    C(IEnumerable<int> items)\n    {\n        var a = items.Count();\n        var b = {|NLR0032:items|}.Sum();\n    }\n}\n");

    // --- Reported on locals, with fix ----------------------------------------------

    [Fact]
    public Task LocalDeferredQuery() => FixAsync(
        Class("        var q = items.Where(i => i > 0);\n        var n = q.Count();\n        foreach (var i in {|NLR0032:q|})\n        {\n        }\n"),
        Class("        var q = items.Where(i => i > 0).ToList();\n        var n = q.Count();\n        foreach (var i in q)\n        {\n        }\n"));

    [Fact]
    public Task LocalWithExplicitTypeAndComments() => FixAsync(
        Class("        // the query\n        IEnumerable<int> q = Source(); // trailing\n        var a = q.Any();\n        var b = {|NLR0032:q|}.ToArray();\n"),
        Class("        // the query\n        IEnumerable<int> q = Source().ToList(); // trailing\n        var a = q.Any();\n        var b = q.ToArray();\n"));

    [Fact]
    public Task InitializerNeedsParentheses() => FixAsync(
        Class("        var q = flag ? items : Source();\n        var a = q.Any();\n        var b = {|NLR0032:q|}.ToArray();\n"),
        Class("        var q = (flag ? items : Source()).ToList();\n        var a = q.Any();\n        var b = q.ToArray();\n"));

    [Fact]
    public Task TwoLocalsFixAll() => FixAsync(
        Class("        var p = Source().Select(i => i);\n        var q = Source().Where(i => i > 0);\n        var a = p.Any() && q.Any();\n        var b = {|NLR0032:p|}.Count() + {|NLR0032:q|}.Count();\n"),
        Class("        var p = Source().Select(i => i).ToList();\n        var q = Source().Where(i => i > 0).ToList();\n        var a = p.Any() && q.Any();\n        var b = p.Count() + q.Count();\n"));

    [Fact]
    public Task LocalInLoopDeclaredOutside() => FixAsync(
        Class("        var q = items.Where(i => i > 0);\n        foreach (var i in list)\n        {\n            var c = {|NLR0032:q|}.Count();\n        }\n"),
        Class("        var q = items.Where(i => i > 0).ToList();\n        foreach (var i in list)\n        {\n            var c = q.Count();\n        }\n"));

    [Fact]
    public Task QueryableLocalHasNoFix() => NoDiagnosticOrFix(
        Class("        var q = query.Where(i => i > 0);\n        var a = q.Count();\n        var b = {|NLR0032:q|}.ToList();\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task NotReportedSingleEnumeration() => NoDiagnosticAsync(
        Class("        var n = items.Count();\n"));

    [Fact]
    public Task NotReportedDifferentBranches() => NoDiagnosticAsync(
        Class("        if (flag)\n        {\n            var a = items.Count();\n        }\n        else\n        {\n            var b = items.ToList();\n        }\n"));

    [Fact]
    public Task NotReportedBranchThatReturns() => NoDiagnosticAsync(
        Class("        if (flag)\n        {\n            var a = items.ToList();\n            return;\n        }\n\n        var b = items.Count();\n"));

    [Fact]
    public Task NotReportedConditionalExpression() => NoDiagnosticAsync(
        Class("        var n = flag ? items.Count() : items.Sum();\n"));

    [Fact]
    public Task NotReportedMaterializedTypes() => NoDiagnosticAsync(
        Class("        var a = list.Count() + list.Sum();\n        var b = array.Count() + array.Sum();\n        var l = items.ToList();\n        var c = l.Count() + l.Sum();\n        IEnumerable<int> d = list;\n        var e = d.Count() + d.Sum();\n"));

    [Fact]
    public Task NotReportedDeferredOnlyOrPassedOn() => NoDiagnosticAsync(
        Class("        var a = items.Where(i => i > 0);\n        var b = items.Select(i => i);\n        var s = string.Join(\",\", items) + string.Join(\",\", items);\n"));

    [Fact]
    public Task NotReportedLocalDeclaredInLoop() => NoDiagnosticAsync(
        Class("        foreach (var i in list)\n        {\n            var q = Source();\n            var c = q.Count();\n        }\n"));

    [Fact]
    public Task NotReportedReassigned() => NoDiagnosticAsync(
        Class("        var a = items.Count();\n        items = items.ToList();\n        var b = items.Count();\n"));

    [Fact]
    public Task NotReportedAfterTypeTest() => NoDiagnosticAsync(
        Class("        if (items is ICollection<int>)\n        {\n        }\n\n        var a = items.Count();\n        var b = items.Sum();\n"));

    [Fact]
    public Task NotReportedInLambda() => NoDiagnosticAsync(
        Class("        Func<int> f = () => items.Count() + items.Sum();\n"));

    [Fact]
    public Task NotReportedFieldOrProperty() => NoDiagnosticAsync(
        "using System.Collections.Generic;\nusing System.Linq;\n\nclass C\n{\n    IEnumerable<int> field = new List<int>();\n    IEnumerable<int> Prop => field;\n\n    int M() => field.Count() + field.Sum() + Prop.Count() + Prop.Sum();\n}\n");

    [Fact]
    public Task NotReportedIteratorForeachOnce() => NoDiagnosticAsync(
        Class("        foreach (var i in items)\n        {\n            Console.WriteLine(i);\n        }\n"));

    private static Task NoDiagnosticOrFix(string source) => new Test(source, source).RunAsync();
}
