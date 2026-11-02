using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.PatternCast;
using Neolinter.CodeFixes.PatternCast;

namespace Neolinter.Tests.PatternCast;

/// <summary>NLR0016 tests. Several diagnostics in one source also exercise fix all.</summary>
public class PatternCastTests
{
    private static Task Run(string source, string fixedSource, LanguageVersion languageVersion)
    {
        var test = new CSharpCodeFixTest<PatternCastAnalyzer, PatternCastCodeFixProvider, DefaultVerifier>
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
        using System.Linq.Expressions;

        class Animal { }
        class Dog : Animal { public int Age; public void Bark() { } }
        class Money
        {
            public static bool operator ==(Money a, Money b) => true;
            public static bool operator !=(Money a, Money b) => false;
            public override bool Equals(object o) => true;
            public override int GetHashCode() => 0;
        }

        class C
        {
            object _f;
            object P { get; set; }

            int M(object o, Animal a, string s, string other, int n)
            {
        {{body}}    }
        }
        """;

    // --- Reported: as, then a null check ---------------------------------------

    [Fact]
    public Task AsNotNull() => FixAsync(
        Method("        var dog = {|NLR0016:o as Dog|};\n        if (dog != null)\n        {\n            dog.Bark();\n        }\n\n        return 0;\n"),
        Method("        if (o is Dog dog)\n        {\n            dog.Bark();\n        }\n\n        return 0;\n"));

    [Theory]
    [InlineData("null != dog")]
    [InlineData("dog is not null")]
    public Task AsNotNullForms(string check) => FixAsync(
        Method($"        var dog = {{|NLR0016:o as Dog|}};\n        if ({check})\n        {{\n            return dog.Age;\n        }}\n\n        return 0;\n"),
        Method("        if (o is Dog dog)\n        {\n            return dog.Age;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task AsNotNullAndMore() => FixAsync(
        Method("        var dog = {|NLR0016:o as Dog|};\n        if (dog != null && dog.Age > 2)\n        {\n            return 1;\n        }\n\n        return 0;\n"),
        Method("        if (o is Dog dog && dog.Age > 2)\n        {\n            return 1;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task AsNotNullWithElse() => FixAsync(
        Method("        var dog = {|NLR0016:o as Dog|};\n        if (dog != null)\n            return dog.Age;\n        else\n            return -1;\n"),
        Method("        if (o is Dog dog)\n            return dog.Age;\n        else\n            return -1;\n"));

    [Fact]
    public Task AsExplicitTypeAndString() => FixAsync(
        Method("        string text = {|NLR0016:o as string|};\n        if (text != null)\n        {\n            return text.Length;\n        }\n\n        return 0;\n"),
        Method("        if (o is string text)\n        {\n            return text.Length;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task AsKeepsCommentAndExpression() => FixAsync(
        Method("        // Barking dogs.\n        var dog = {|NLR0016:Choose(o, a) as Dog|};\n        if (dog != null)\n        {\n            dog.Bark();\n        }\n\n        return 0;\n    }\n\n    object Choose(object x, object y)\n    {\n        return x;\n"),
        Method("        // Barking dogs.\n        if (Choose(o, a) is Dog dog)\n        {\n            dog.Bark();\n        }\n\n        return 0;\n    }\n\n    object Choose(object x, object y)\n    {\n        return x;\n"));

    // --- Reported: as, then a guard clause (C# 9) -----------------------------

    [Theory]
    [InlineData("dog == null")]
    [InlineData("null == dog")]
    [InlineData("dog is null")]
    public Task AsGuard(string check) => FixAsync(
        Method($"        var dog = {{|NLR0016:o as Dog|}};\n        if ({check})\n        {{\n            return 0;\n        }}\n\n        return dog.Age;\n"),
        Method("        if (o is not Dog dog)\n        {\n            return 0;\n        }\n\n        return dog.Age;\n"));

    [Fact]
    public Task AsGuardThrow() => FixAsync(
        Method("        var dog = {|NLR0016:o as Dog|};\n        if (dog == null)\n            throw new ArgumentException(nameof(o));\n\n        dog.Bark();\n        return 0;\n"),
        Method("        if (o is not Dog dog)\n            throw new ArgumentException(nameof(o));\n\n        dog.Bark();\n        return 0;\n"));

    [Fact]
    public Task AsGuardNotReportedBeforeCSharp9() => NoDiagnosticAsync(
        Method("        var dog = o as Dog;\n        if (dog == null)\n        {\n            return 0;\n        }\n\n        return dog.Age;\n"),
        LanguageVersion.CSharp8);

    // --- Reported: is, then a cast ---------------------------------------------

    [Fact]
    public Task IsCast() => FixAsync(
        Method("        if ({|NLR0016:o is Dog|})\n        {\n            ((Dog)o).Bark();\n            return ((Dog)o).Age;\n        }\n\n        return 0;\n"),
        Method("        if (o is Dog dog)\n        {\n            dog.Bark();\n            return dog.Age;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task IsCastWithoutBraces() => FixAsync(
        Method("        if ({|NLR0016:a is Dog|})\n            return ((Dog)a).Age;\n\n        return 0;\n"),
        Method("        if (a is Dog dog)\n            return dog.Age;\n\n        return 0;\n"));

    [Fact]
    public Task IsCastBoxedValueAndKeyword() => FixAsync(
        Method("        if ({|NLR0016:o is int|})\n        {\n            return (int)o + 1;\n        }\n\n        if ({|NLR0016:o is string|})\n        {\n            return ((string)o).Length;\n        }\n\n        return 0;\n"),
        Method("        if (o is int int32)\n        {\n            return int32 + 1;\n        }\n\n        if (o is string stringValue)\n        {\n            return stringValue.Length;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task IsCastNameIsTaken() => FixAsync(
        Method("        var dog = 1;\n        if ({|NLR0016:o is Dog|})\n        {\n            return ((Dog)o).Age + dog;\n        }\n\n        return 0;\n"),
        Method("        var dog = 1;\n        if (o is Dog dog1)\n        {\n            return dog1.Age + dog;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task IsCastFieldWithNoCallBefore() => FixAsync(
        Method("        if ({|NLR0016:_f is Dog|})\n        {\n            return ((Dog)_f).Age;\n        }\n\n        return 0;\n"),
        Method("        if (_f is Dog dog)\n        {\n            return dog.Age;\n        }\n\n        return 0;\n"));

    [Fact]
    public Task IsCastTwoSiblingsGetDistinctNames() => FixAsync(
        Method("        if ({|NLR0016:o is Dog|})\n        {\n            ((Dog)o).Bark();\n        }\n\n        if ({|NLR0016:a is Dog|})\n        {\n            ((Dog)a).Bark();\n        }\n\n        return 0;\n"),
        Method("        if (o is Dog dog)\n        {\n            dog.Bark();\n        }\n\n        if (a is Dog dog1)\n        {\n            dog1.Bark();\n        }\n\n        return 0;\n"));

    [Fact]
    public Task IsCastKeepsComment() => FixAsync(
        Method("        // Only dogs bark.\n        if ({|NLR0016:o is Dog|}) // really\n        {\n            ((Dog)o).Bark(); // woof\n        }\n\n        return 0;\n"),
        Method("        // Only dogs bark.\n        if (o is Dog dog) // really\n        {\n            dog.Bark(); // woof\n        }\n\n        return 0;\n"));

    // --- Not reported ----------------------------------------------------------

    [Theory]
    // as: the variable is used after the if, or in the else, or the check is not a plain null test
    [InlineData("        var dog = o as Dog;\n        if (dog != null)\n        {\n            dog.Bark();\n        }\n\n        return dog == null ? 0 : 1;\n")]
    [InlineData("        var dog = o as Dog;\n        if (dog != null)\n        {\n            return 1;\n        }\n        else\n        {\n            return dog == null ? 2 : 3;\n        }\n")]
    [InlineData("        var dog = o as Dog;\n        if (n > 0 && dog != null)\n        {\n            return 1;\n        }\n\n        return 0;\n")]
    [InlineData("        var dog = o as Dog;\n        if (dog == null)\n        {\n            return 0;\n        }\n        else\n        {\n            return dog.Age;\n        }\n")]
    [InlineData("        var dog = o as Dog;\n        n++;\n        if (dog != null)\n        {\n            return dog.Age;\n        }\n\n        return 0;\n")]
    [InlineData("        var dog = o as Dog; // keep\n        if (dog != null)\n        {\n            return dog.Age;\n        }\n\n        return 0;\n")]
    [InlineData("        Dog dog = o as Dog, other2 = o as Dog;\n        if (dog != null)\n        {\n            return dog.Age;\n        }\n\n        return 0;\n")]
    [InlineData("        var dog = o as Dog;\n        if (dog == null)\n        {\n            n++;\n        }\n\n        return dog.Age;\n")]
    [InlineData("        var dog = o as Dog;\n        if (dog == null)\n        {\n            return dog == null ? 1 : 0;\n        }\n\n        return dog.Age;\n")]
    [InlineData("        var money = o as Money;\n        if (money != null)\n        {\n            return 1;\n        }\n\n        return 0;\n")]
    [InlineData("        var dog = o as Dog;\n        Expression<Func<int>> f = () => dog.Age;\n        if (dog != null)\n        {\n            return 1;\n        }\n\n        return 0;\n")]
    // is: other conversion, other type, written or moved value, property, nested type checks, call before the cast on a field
    [InlineData("        if (o is Dog)\n        {\n            return 1;\n        }\n\n        return 0;\n")]
    [InlineData("        if (o is Dog)\n        {\n            return ((Animal)o) == null ? 1 : 0;\n        }\n\n        return 0;\n")]
    [InlineData("        if (o is Dog)\n        {\n            o = a;\n            return ((Dog)o).Age;\n        }\n\n        return 0;\n")]
    [InlineData("        if (P is Dog)\n        {\n            return ((Dog)P).Age;\n        }\n\n        return 0;\n")]
    [InlineData("        if (_f is Dog)\n        {\n            Console.WriteLine();\n            return ((Dog)_f).Age;\n        }\n\n        return 0;\n")]
    [InlineData("        if (o is Dog)\n        {\n            Func<int> f = () => ((Dog)o).Age;\n            return f();\n        }\n\n        return 0;\n")]
    [InlineData("        if (o is Dog && n > 0)\n        {\n            return ((Dog)o).Age;\n        }\n\n        return 0;\n")]
    [InlineData("        if (o is Dog)\n        {\n            return ((Dog)o /* the dog */).Age;\n        }\n\n        return 0;\n")]
    public Task NotReported(string code) => NoDiagnosticAsync(Method(code));

    [Fact]
    public Task NotReportedBeforeCSharp7() => NoDiagnosticAsync(
        Method("        var dog = o as Dog;\n        if (dog != null)\n        {\n            return dog.Age;\n        }\n\n        if (o is Dog)\n        {\n            return ((Dog)o).Age;\n        }\n\n        return 0;\n"),
        LanguageVersion.CSharp6);

    [Fact]
    public Task NotReportedInExpressionTree() => NoDiagnosticAsync(
        Method("        Expression<Func<object, int>> f = x => x is Dog ? ((Dog)x).Age : 0;\n        return 0;\n"));
}
