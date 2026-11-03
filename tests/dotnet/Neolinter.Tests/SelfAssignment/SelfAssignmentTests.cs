using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.SelfAssignment;
using Neolinter.CodeFixes.SelfAssignment;

namespace Neolinter.Tests.SelfAssignment;

/// <summary>NLR0029 tests. Several diagnostics in one source also exercise fix all.</summary>
public class SelfAssignmentTests
{
    private sealed class Test : CSharpCodeFixTest<SelfAssignmentAnalyzer, SelfAssignmentCodeFixProvider, DefaultVerifier>
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
        "class Other\n{\n    public int A;\n}\n\nclass C\n{\n    int count;\n    int Auto { get; set; }\n    int Body { get => count; set => count = value; }\n    public virtual int Virtual { get; set; }\n    Other other = new();\n    static int total;\n\n    Other Get() => other;\n\n    void M(int count, Other o, Other p, int[] items, int i)\n    {\n" + body + "    }\n}\n";

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task LocalOrParameter() => FixAsync(
        Class("        {|NLR0029:count = count|};\n        int x = 1;\n"),
        Class("        int x = 1;\n"));

    [Fact]
    public Task ThisFieldAssignedToItself() => FixAsync(
        Class("        {|NLR0029:this.count = this.count|};\n"),
        Class(""));

    [Fact]
    public Task ParenthesizedAndStatic() => FixAsync(
        Class("        {|NLR0029:this.count = (this.count)|};\n        {|NLR0029:C.total = C.total|};\n"),
        Class(""));

    [Fact]
    public Task AutoProperty() => FixAsync(
        Class("        {|NLR0029:Auto = this.Auto|};\n"),
        Class(""));

    [Fact]
    public Task SameReceiver() => FixAsync(
        Class("        {|NLR0029:o.A = o.A|};\n        {|NLR0029:other.A = this.other.A|};\n"),
        Class(""));

    [Fact]
    public Task KeepsComments() => FixAsync(
        Class("        // before\n        {|NLR0029:count = count|}; // trailing\n        // after\n        int x = 1;\n"),
        Class("        // before\n        // after\n        int x = 1;\n"));

    [Fact]
    public Task SeveralSelfAssignmentsFixAll() => FixAsync(
        Class("        {|NLR0029:count = count|};\n        int x = 1;\n        {|NLR0029:o.A = o.A|};\n"),
        Class("        int x = 1;\n"));

    [Fact]
    public Task ReportedWithoutFixWhenNotAStatementOfABlock() => FixAsync(
        Class("        if (i > 0)\n            {|NLR0029:count = count|};\n        int y = ({|NLR0029:count = count|});\n"),
        Class("        if (i > 0)\n            {|NLR0029:count = count|};\n        int y = ({|NLR0029:count = count|});\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task DifferentMembers() => NoDiagnosticAsync(Class("        this.count = count;\n        count = this.count;\n"));

    [Fact]
    public Task DifferentReceivers() => NoDiagnosticAsync(Class("        o.A = p.A;\n        o.A = other.A;\n"));

    [Fact]
    public Task CompoundAssignments() => NoDiagnosticAsync(Class("        count += count;\n        count -= count;\n        count |= count;\n"));

    [Fact]
    public Task PropertyWithBody() => NoDiagnosticAsync(Class("        Body = Body;\n"));

    [Fact]
    public Task VirtualProperty() => NoDiagnosticAsync(Class("        Virtual = Virtual;\n"));

    [Fact]
    public Task CallInReceiver() => NoDiagnosticAsync(Class("        Get().A = Get().A;\n"));

    [Fact]
    public Task IndexerAndElement() => NoDiagnosticAsync(Class("        items[i] = items[i];\n        items[i++] = items[i++];\n"));

    [Fact]
    public Task ChainWithBodyProperty() => NoDiagnosticAsync(Class("        Body = Body + 0;\n"));
}
