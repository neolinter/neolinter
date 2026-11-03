using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.IgnoredReturnValue;
using Neolinter.CodeFixes.IgnoredReturnValue;

namespace Neolinter.Tests.IgnoredReturnValue;

/// <summary>NLR0030 tests. Several diagnostics in one source also exercise fix all.</summary>
public class IgnoredReturnValueTests
{
    private sealed class Test : CSharpCodeFixTest<IgnoredReturnValueAnalyzer, IgnoredReturnValueCodeFixProvider, DefaultVerifier>
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
        "using System;\nusing System.Collections.Generic;\nusing System.Collections.Immutable;\nusing System.Diagnostics.Contracts;\nusing System.Linq;\n\nclass C\n{\n    string name = \"\";\n    readonly string fixedName = \"\";\n    string Prop { get; set; } = \"\";\n\n    [Pure]\n    static int Twice(int v) => v * 2;\n\n    static int Plain(int v) => v;\n\n    void M(string s, DateTime d, TimeSpan t, List<int> list, ImmutableArray<int> arr, int[] items)\n    {\n" + body + "    }\n}\n";

    // --- Reported with fix ---------------------------------------------------------

    [Fact]
    public Task StringReplaceOnParameter() => FixAsync(
        Class("        {|NLR0030:s.Replace(\"a\", \"b\")|};\n"),
        Class("        s = s.Replace(\"a\", \"b\");\n"));

    [Fact]
    public Task FieldAndThisField() => FixAsync(
        Class("        {|NLR0030:name.Trim()|};\n        {|NLR0030:this.name.ToUpper()|};\n"),
        Class("        name = name.Trim();\n        this.name = this.name.ToUpper();\n"));

    [Fact]
    public Task DatesAndImmutable() => FixAsync(
        Class("        {|NLR0030:d.AddDays(1)|};\n        {|NLR0030:t.Add(t)|};\n        {|NLR0030:arr.Add(1)|};\n"),
        Class("        d = d.AddDays(1);\n        t = t.Add(t);\n        arr = arr.Add(1);\n"));

    [Fact]
    public Task KeepsCommentsAndIndentation() => FixAsync(
        Class("        // before\n        {|NLR0030:s.Trim()|}; // trailing\n"),
        Class("        // before\n        s = s.Trim(); // trailing\n"));

    // --- Reported without fix ------------------------------------------------------

    [Fact]
    public Task LinqAndMathAndPure() => FixAsync(
        Class("        {|NLR0030:list.Select(i => i * 2)|};\n        {|NLR0030:Math.Abs(1)|};\n        {|NLR0030:Twice(2)|};\n"),
        Class("        {|NLR0030:list.Select(i => i * 2)|};\n        {|NLR0030:Math.Abs(1)|};\n        {|NLR0030:Twice(2)|};\n"));

    [Fact]
    public Task NoFixWhenTypeDiffersOrReceiverCannotBeAssigned() => FixAsync(
        Class("        {|NLR0030:s.Split(',')|};\n        {|NLR0030:fixedName.Trim()|};\n        {|NLR0030:Prop.Trim()|};\n        {|NLR0030:s.Trim().ToUpper()|};\n"),
        Class("        {|NLR0030:s.Split(',')|};\n        {|NLR0030:fixedName.Trim()|};\n        {|NLR0030:Prop.Trim()|};\n        {|NLR0030:s.Trim().ToUpper()|};\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task ResultUsedOrDiscarded() => NoDiagnosticAsync(Class("        s = s.Trim();\n        _ = s.Trim();\n        var x = s.Trim();\n        Console.WriteLine(s.Trim());\n"));

    [Fact]
    public Task MutatingMethods() => NoDiagnosticAsync(Class("        list.Add(1);\n        list.Sort();\n        list.Clear();\n        Array.Sort(items);\n"));

    [Fact]
    public Task UserMethodWithoutAttribute() => NoDiagnosticAsync(Class("        Plain(1);\n"));

    [Fact]
    public Task OutParameter() => NoDiagnosticAsync(Class("        int.TryParse(s, out _);\n"));

    [Fact]
    public Task OtherRulesTargets() => NoDiagnosticAsync(
        Class("        System.Threading.Thread.Sleep(1);\n        System.Threading.Tasks.Task.Delay(1).Wait();\n        s = s;\n"));
}
