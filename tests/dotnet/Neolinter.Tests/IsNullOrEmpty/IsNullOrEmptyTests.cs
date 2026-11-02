using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.IsNullOrEmpty;
using Neolinter.CodeFixes.IsNullOrEmpty;

namespace Neolinter.Tests.IsNullOrEmpty;

/// <summary>NLR0014 tests. Several diagnostics in one source also exercise fix all.</summary>
public class IsNullOrEmptyTests
{
    private static Task Run(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<IsNullOrEmptyAnalyzer, IsNullOrEmptyCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        return test.RunAsync();
    }

    private static Task FixAsync(string source, string fixedSource) => Run(source, fixedSource);

    private static Task NoDiagnosticAsync(string source) => Run(source, source);

    private static string Method(string body) =>
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;

        class C
        {
            string Name { get; set; }
            C Other { get; set; }

            bool M(string s, string t, object o, bool b, int i, IQueryable<string> q, List<string> list)
            {
        {{body}}    }
        }
        """;

    // --- Reported ------------------------------------------------------------

    [Theory]
    [InlineData("s == null || s == \"\"", "string.IsNullOrEmpty(s)")]
    [InlineData("null == s || \"\" == s", "string.IsNullOrEmpty(s)")]
    [InlineData("s is null || s == \"\"", "string.IsNullOrEmpty(s)")]
    [InlineData("s == null || s == string.Empty", "string.IsNullOrEmpty(s)")]
    [InlineData("s == null || string.Empty == s", "string.IsNullOrEmpty(s)")]
    [InlineData("s == null || s == String.Empty", "string.IsNullOrEmpty(s)")]
    [InlineData("s == null || s.Length == 0", "string.IsNullOrEmpty(s)")]
    [InlineData("s is null || 0 == s.Length", "string.IsNullOrEmpty(s)")]
    [InlineData("s != null && s != \"\"", "!string.IsNullOrEmpty(s)")]
    [InlineData("null != s && \"\" != s", "!string.IsNullOrEmpty(s)")]
    [InlineData("s is not null && s != \"\"", "!string.IsNullOrEmpty(s)")]
    [InlineData("s != null && s != string.Empty", "!string.IsNullOrEmpty(s)")]
    [InlineData("s != null && s.Length != 0", "!string.IsNullOrEmpty(s)")]
    [InlineData("s != null && s.Length > 0", "!string.IsNullOrEmpty(s)")]
    [InlineData("Name == null || Name == \"\"", "string.IsNullOrEmpty(Name)")]
    [InlineData("this.Name == null || this.Name == \"\"", "string.IsNullOrEmpty(this.Name)")]
    [InlineData("Other.Name != null && Other.Name != \"\"", "!string.IsNullOrEmpty(Other.Name)")]
    public Task Reported(string condition, string expected) => FixAsync(
        Method($"        var x = {{|NLR0014:{condition}|}};\n        return x;\n"),
        Method($"        var x = {expected};\n        return x;\n"));

    [Fact]
    public Task ChainKeepsTheOperandsAround() => FixAsync(
        Method("        return b || {|NLR0014:s == null || s == \"\"|} || i > 3;\n"),
        Method("        return b || string.IsNullOrEmpty(s) || i > 3;\n"));

    [Fact]
    public Task ChainOfAnds() => FixAsync(
        Method("        return b && {|NLR0014:s != null && s != \"\"|};\n"),
        Method("        return b && !string.IsNullOrEmpty(s);\n"));

    [Fact]
    public Task InsideIf() => FixAsync(
        Method("        if ({|NLR0014:s == null || s == \"\"|})\n        {\n            return true;\n        }\n\n        return false;\n"),
        Method("        if (string.IsNullOrEmpty(s))\n        {\n            return true;\n        }\n\n        return false;\n"));

    [Fact]
    public Task KeepsLayoutAndCommentsAround() => FixAsync(
        Method("        return /* a */ {|NLR0014:s == null ||\n            s == \"\"|}; // b\n"),
        Method("        return /* a */ string.IsNullOrEmpty(s); // b\n"));

    [Fact]
    public Task TwoPairsInOneChain_FixAll() => FixAsync(
        Method("        return {|NLR0014:s == null || s == \"\"|} || {|NLR0014:t == null || t == \"\"|};\n"),
        Method("        return string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t);\n"));

    [Fact]
    public Task TwoPairsInOneUnparenthesizedChain_FixAll() => FixAsync(
        Method("        return {|NLR0014:s == null || s == \"\"|} || {|NLR0014:t == null || t == \"\"|} || b;\n"),
        Method("        return string.IsNullOrEmpty(s) || string.IsNullOrEmpty(t) || b;\n"));

    [Fact]
    public Task SeveralInOneSource_FixAll() => FixAsync(
        Method("        var a = {|NLR0014:s == null || s == \"\"|};\n        var c = {|NLR0014:t != null && t.Length > 0|};\n        return a || c;\n"),
        Method("        var a = string.IsNullOrEmpty(s);\n        var c = !string.IsNullOrEmpty(t);\n        return a || c;\n"));

    // --- Not reported --------------------------------------------------------

    [Theory]
    [InlineData("s == null || t == \"\"")]
    [InlineData("s == null || s == \"a\"")]
    [InlineData("s == null || s == \" \"")]
    [InlineData("s == null || s.Length == 1")]
    [InlineData("s == null || s.Length > 0")]
    [InlineData("s != null && s == \"\"")]
    [InlineData("s != null && s.Length == 0")]
    [InlineData("s == null && s == \"\"")]
    [InlineData("s == null || s != \"\"")]
    [InlineData("s != null || s != \"\"")]
    [InlineData("s == \"\" || s == null")]
    [InlineData("s.Length == 0 || s == null")]
    [InlineData("s == null || s.Trim() == \"\"")]
    [InlineData("s.Trim() == null || s.Trim() == \"\"")]
    [InlineData("Other.Other.Name == null || Other.Name == \"\"")]
    [InlineData("s == null || string.IsNullOrWhiteSpace(s)")]
    [InlineData("o == null || o == \"\"")]
    [InlineData("(s == null) || (s == \"\")")]
    [InlineData("s == null || s /* why */ == \"\"")]
    [InlineData("s == null /* why */ || s == \"\"")]
    public Task NotReported(string condition) => NoDiagnosticAsync(Method($"        return {condition};\n"));

    [Fact]
    public Task ReceiverWithACall_NoDiagnostic() => NoDiagnosticAsync(
        Method("        return list[0] == null || list[0] == \"\";\n"));

    [Fact]
    public Task ReceiverThatIsAMethodResult_NoDiagnostic() => NoDiagnosticAsync(
        Method("        return Name.ToString() == null || Name.ToString() == \"\";\n"));

    [Fact]
    public Task ExpressionTree_NoDiagnostic() => NoDiagnosticAsync(
        Method("        Expression<Func<string, bool>> e = x => x == null || x == \"\";\n        return q.Where(x => x != null && x != \"\").Any();\n"));

    [Fact]
    public Task DirectiveInside_NoDiagnostic() => NoDiagnosticAsync(
        Method("        return s == null ||\n#if DEBUG\n            s == \"\"\n#else\n            s == \"\"\n#endif\n            ;\n"));

    [Fact]
    public Task DelegateOverEnumerableIsReported() => FixAsync(
        Method("        return list.Any(x => {|NLR0014:x == null || x == \"\"|});\n"),
        Method("        return list.Any(x => string.IsNullOrEmpty(x));\n"));
}
