using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.IsNull;
using Neolinter.CodeFixes.IsNull;

namespace Neolinter.Tests.IsNull;

/// <summary>NLR0013 tests. Several diagnostics in one source also exercise fix all.</summary>
public class IsNullTests
{
    private static Task Run(string source, string fixedSource, LanguageVersion languageVersion)
    {
        var test = new CSharpCodeFixTest<IsNullAnalyzer, IsNullCodeFixProvider, DefaultVerifier>
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

    private static string Method(string body) =>
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;

        class Own
        {
            public static bool operator ==(Own a, Own b) => true;
            public static bool operator !=(Own a, Own b) => false;
            public override bool Equals(object o) => true;
            public override int GetHashCode() => 0;
        }

        struct Val
        {
            public static bool operator ==(Val a, Val b) => true;
            public static bool operator !=(Val a, Val b) => false;
            public override bool Equals(object o) => true;
            public override int GetHashCode() => 0;
        }

        class C
        {
            string Name { get; set; }

            bool M<T>(string s, object o, int? n, Val? v, Own own, int i, T t, IQueryable<string> q, List<string> list, bool b, Action act)
            {
        {{body}}    }
        }
        """;

    // --- Reported ------------------------------------------------------------

    [Theory]
    [InlineData("s == null", "s is null")]
    [InlineData("null == s", "s is null")]
    [InlineData("s != null", "s is not null")]
    [InlineData("null != s", "s is not null")]
    [InlineData("o == null", "o is null")]
    [InlineData("n == null", "n is null")]
    [InlineData("n != null", "n is not null")]
    [InlineData("v == null", "v is null")]
    [InlineData("t == null", "t is null")]
    [InlineData("Name == null", "Name is null")]
    [InlineData("list[0] != null", "list[0] is not null")]
    [InlineData("s.Trim() == null", "s.Trim() is null")]
    [InlineData("(s ?? o.ToString()) == null", "(s ?? o.ToString()) is null")]
    [InlineData("(b ? s : null) == null", "(b ? s : null) is null")]
    public Task Reported(string condition, string expected) => FixAsync(
        Method($"        var x = {{|NLR0013:{condition}|}};\n        return x;\n"),
        Method($"        var x = {expected};\n        return x;\n"));

    [Fact]
    public Task OperandWithAnOperator() => FixAsync(
        Method("        return {|NLR0013:o as string == null|};\n"),
        Method("        return o as string is null;\n"));

    [Fact]
    public Task InsideLargerCondition() => FixAsync(
        Method("        return {|NLR0013:s == null|} && {|NLR0013:o != null|} || {|NLR0013:null == list|};\n"),
        Method("        return s is null && o is not null || list is null;\n"));

    [Fact]
    public Task InsideTernary() => FixAsync(
        Method("        return {|NLR0013:s == null|} ? true : b;\n"),
        Method("        return s is null ? true : b;\n"));

    [Fact]
    public Task QueryableDelegateIsNotAnExpressionTree() => FixAsync(
        Method("        return list.Any(x => {|NLR0013:x == null|});\n"),
        Method("        return list.Any(x => x is null);\n"));

    [Fact]
    public Task QueryOverEnumerable() => FixAsync(
        Method("        return (from x in list where {|NLR0013:x != null|} select x).Any();\n"),
        Method("        return (from x in list where x is not null select x).Any();\n"));

    [Fact]
    public Task KeepsLayoutAndCommentsAround() => FixAsync(
        Method("        return /* a */ {|NLR0013:s\n            == null|}; // b\n"),
        Method("        return /* a */ s is null; // b\n"));

    [Fact]
    public Task IsNullOnCSharp7() => FixAsync(
        Method("        return {|NLR0013:s == null|};\n"),
        Method("        return s is null;\n"),
        LanguageVersion.CSharp7_3);

    // --- Not reported --------------------------------------------------------

    [Theory]
    [InlineData("own == null")]
    [InlineData("null != own")]
    [InlineData("i == null")]
    [InlineData("null == null")]
    [InlineData("s == Name")]
    [InlineData("s is null")]
    [InlineData("s is not null")]
    [InlineData("s /* why */ == null")]
    [InlineData("null /* why */ != s")]
    public Task NotReported(string condition) => NoDiagnosticAsync(Method($"        return {condition};\n"));

    [Fact]
    public Task ExpressionTreeLambda_NoDiagnostic() => NoDiagnosticAsync(
        Method("        Expression<Func<string, bool>> e = x => x == null;\n        return q.Where(x => x != null).Any();\n"));

    [Fact]
    public Task ExpressionTreeQuery_NoDiagnostic() => NoDiagnosticAsync(
        Method("        return (from x in q where x != null select x).Any();\n"));

    [Fact]
    public Task Dynamic_NoDiagnostic() => NoDiagnosticAsync(
        Method("        dynamic d = o;\n        return d == null;\n"));

    [Fact]
    public Task NotEqualsBeforeCSharp9_NoDiagnostic() => NoDiagnosticAsync(
        Method("        return s != null;\n"),
        LanguageVersion.CSharp8);

    [Fact]
    public Task EqualsBeforeCSharp7_NoDiagnostic() => NoDiagnosticAsync(
        Method("        return s == null;\n"),
        LanguageVersion.CSharp6);

    [Fact]
    public Task DirectiveInside_NoDiagnostic() => NoDiagnosticAsync(
        Method("        return s ==\n#if DEBUG\n            null\n#else\n            null\n#endif\n            ;\n"));

    [Fact]
    public Task FixAllSeveralInOneSource() => FixAsync(
        Method("        var a = {|NLR0013:s == null|};\n        var c = {|NLR0013:null != o|};\n        return a && {|NLR0013:n == null|} || c;\n"),
        Method("        var a = s is null;\n        var c = o is not null;\n        return a && n is null || c;\n"));
}
