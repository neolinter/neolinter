using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.StringFormat;
using Neolinter.CodeFixes.StringFormat;

namespace Neolinter.Tests.StringFormat;

/// <summary>NLR0022 tests. Several diagnostics in one source also exercise fix all.</summary>
public class StringFormatTests
{
    private sealed class Test : CSharpCodeFixTest<StringFormatAnalyzer, StringFormatCodeFixProvider, DefaultVerifier>
    {
        public Test(string source, string fixedSource, LanguageVersion version)
        {
            TestCode = source;
            FixedCode = fixedSource;
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
            SolutionTransforms.Add((solution, projectId) =>
            {
                var options = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
                return solution.WithProjectParseOptions(projectId, options.WithLanguageVersion(version));
            });
        }
    }

    private static Task FixAsync(string source, string fixedSource, LanguageVersion version = LanguageVersion.Latest) =>
        new Test(source, fixedSource, version).RunAsync();

    private static Task NoDiagnosticAsync(string source, LanguageVersion version = LanguageVersion.Latest) =>
        new Test(source, source, version).RunAsync();

    private static string Class(string body) =>
        "using System;\nusing System.Globalization;\nusing System.Linq;\nusing System.Linq.Expressions;\n\nclass C\n{\n    string Name = \"\";\n    C Other;\n\n    int F() { return 1; }\n\n    void M(int n, string s, object o, bool c, object[] a, DateTime d)\n    {\n        string t;\n" + body + "    }\n}\n";

    private static Task Reported(string before, string after, LanguageVersion version = LanguageVersion.Latest) =>
        FixAsync(Class("        t = {|NLR0022:" + before + "|};\n"), Class("        t = " + after + ";\n"), version);

    private static Task NotReported(string expression, LanguageVersion version = LanguageVersion.Latest) =>
        NoDiagnosticAsync(Class("        t = " + expression + ";\n"), version);

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task OnePlaceholder() => Reported("string.Format(\"a{0}b\", s)", "$\"a{s}b\"");

    [Fact]
    public Task TwoPlaceholders() => Reported("string.Format(\"a{0}b{1}\", s, n)", "$\"a{s}b{n}\"");

    [Fact]
    public Task StringClassName() => Reported("String.Format(\"{0}!\", s)", "$\"{s}!\"");

    [Fact]
    public Task AnyOrder() => Reported("string.Format(\"{1}-{0}\", s, n)", "$\"{n}-{s}\"");

    [Fact]
    public Task RepeatedPlaceholder() => Reported("string.Format(\"{0}{0}\", s)", "$\"{s}{s}\"");

    [Fact]
    public Task RepeatedMemberAccessChain() => Reported("string.Format(\"{0}/{0}\", Other.Name)", "$\"{Other.Name}/{Other.Name}\"");

    [Fact]
    public Task Alignment() => Reported("string.Format(\"{0,10}|{0,-5}\", s)", "$\"{s,10}|{s,-5}\"");

    [Fact]
    public Task FormatSpecifier() => Reported("string.Format(\"{0:F2}\", n)", "$\"{n:F2}\"");

    [Fact]
    public Task AlignmentAndSpecifier() => Reported("string.Format(\"{0,10:F2}\", n)", "$\"{n,10:F2}\"");

    [Fact]
    public Task SpecifierWithColon() => Reported("string.Format(\"{0:hh:mm}\", d)", "$\"{d:hh:mm}\"");

    [Fact]
    public Task DoubledBracesAreKept() => Reported("string.Format(\"{{{0}}}\", s)", "$\"{{{s}}}\"");

    [Fact]
    public Task EscapesAreKept() => Reported("string.Format(\"\\\"{0}\\\"\\n\", s)", "$\"\\\"{s}\\\"\\n\"");

    [Fact]
    public Task ConditionalArgumentIsParenthesized() => Reported("string.Format(\"{0}\", c ? s : \"x\")", "$\"{(c ? s : \"x\")}\"");

    [Fact]
    public Task CallArgumentUsedOnceInOrder() => Reported("string.Format(\"{0}{1}\", F(), s)", "$\"{F()}{s}\"");

    [Fact]
    public Task ManyArguments() => Reported("string.Format(\"{0}{1}{2}{3}\", s, n, o, c)", "$\"{s}{n}{o}{c}\"");

    [Fact]
    public Task CSharp6IsEnough() => Reported("string.Format(\"{0}\", s)", "$\"{s}\"", LanguageVersion.CSharp6);

    [Fact]
    public Task FixAll() => FixAsync(
        Class("        t = {|NLR0022:string.Format(\"a{0}\", s)|};\n        t = {|NLR0022:string.Format(\"{0}{1}\", n, s)|};\n"),
        Class("        t = $\"a{s}\";\n        t = $\"{n}{s}\";\n"));

    [Fact]
    public Task TriviaIsKept() => FixAsync(
        Class("        t =   {|NLR0022:string.Format(\"{0}\", s)|}  ; // end\n"),
        Class("        t =   $\"{s}\"  ; // end\n"));

    [Fact]
    public Task InnerCallGoesFirst() => FixAsync(
        Class("        t = string.Format(\"{0}\", {|NLR0022:string.Format(\"{0}\", s)|});\n"),
        Class("        t = string.Format(\"{0}\", $\"{s}\");\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task CultureOverloadIsNotReported() => NotReported("string.Format(CultureInfo.InvariantCulture, \"{0}\", s)");

    [Fact]
    public Task NonConstantFormatIsNotReported() => NotReported("string.Format(s, n)");

    [Fact]
    public Task ConstFormatIsNotReported() => NoDiagnosticAsync(Class("        const string f = \"{0}\";\n        t = string.Format(f, s);\n"));

    [Fact]
    public Task ArrayArgumentIsNotReported() => NotReported("string.Format(\"{0}{1}\", a)");

    [Fact]
    public Task UnusedArgumentIsNotReported() => NotReported("string.Format(\"{0}\", s, n)");

    [Fact]
    public Task MissingArgumentIsNotReported() => NotReported("string.Format(\"{0}{1}\", s)");

    [Fact]
    public Task SkippedIndexIsNotReported() => NotReported("string.Format(\"{1}\", s, n)");

    [Fact]
    public Task NoPlaceholderIsNotReported() => NotReported("string.Format(\"a\", s)");

    [Fact]
    public Task InvalidFormatIsNotReported() => NotReported("string.Format(\"{0\", s)");

    [Fact]
    public Task LoneClosingBraceIsNotReported() => NotReported("string.Format(\"{0}}\", s)");

    [Fact]
    public Task WhitespaceInPlaceholderIsNotReported() => NotReported("string.Format(\"{0 }\", s)");

    [Fact]
    public Task VerbatimFormatIsNotReported() => NotReported("string.Format(@\"{0}\", s)");

    [Fact]
    public Task UnicodeEscapeIsNotReported() => NotReported("string.Format(\"\\u007B0}\", s)");

    [Fact]
    public Task RepeatedCallIsNotReported() => NotReported("string.Format(\"{0}{0}\", F())");

    [Fact]
    public Task CallsOutOfOrderAreNotReported() => NotReported("string.Format(\"{1}{0}\", F(), F())");

    [Fact]
    public Task NullArgumentIsNotReported() => NotReported("string.Format(\"{0}\", null)");

    [Fact]
    public Task NamedArgumentIsNotReported() => NotReported("string.Format(\"{0}\", arg0: s)");

    [Fact]
    public Task CommentIsNotReported() => NotReported("string.Format(\"{0}\" /* c */, s)");

    [Fact]
    public Task OtherFormatMethodIsNotReported() => NoDiagnosticAsync(
        "class C\n{\n    static string Format(string f, object x) => f;\n\n    string M(string s) => Format(\"{0}\", s);\n}\n");

    [Fact]
    public Task ExpressionTreeIsNotReported() => NoDiagnosticAsync(
        Class("        Expression<Func<string, string>> e = x => string.Format(\"{0}\", x);\n"));

    [Fact]
    public Task OlderLanguageIsNotReported() => NotReported("string.Format(\"{0}\", s)", LanguageVersion.CSharp5);
}
