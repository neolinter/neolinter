using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.StringInterpolation;
using Neolinter.CodeFixes.StringInterpolation;

namespace Neolinter.Tests.StringInterpolation;

/// <summary>NLR0021 tests. Several diagnostics in one source also exercise fix all.</summary>
public class StringInterpolationTests
{
    private sealed class Test : CSharpCodeFixTest<StringInterpolationAnalyzer, StringInterpolationCodeFixProvider, DefaultVerifier>
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
        "using System;\nusing System.Linq;\nusing System.Linq.Expressions;\n\nclass C\n{\n    const string K = \"k\";\n    string Name = \"\";\n\n    void M(int n, string s, object o, bool c, int[] a)\n    {\n        string t;\n" + body + "    }\n}\n";

    private static Task Reported(string before, string after, LanguageVersion version = LanguageVersion.Latest) =>
        FixAsync(Class("        t = {|NLR0021:" + before + "|};\n"), Class("        t = " + after + ";\n"), version);

    private static Task NotReported(string expression, LanguageVersion version = LanguageVersion.Latest) =>
        NoDiagnosticAsync(Class("        t = " + expression + ";\n"), version);

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task LiteralAndVariable() => Reported("\"a\" + s", "$\"a{s}\"");

    [Fact]
    public Task VariableAndLiteral() => Reported("s + \"a\"", "$\"{s}a\"");

    [Fact]
    public Task LongChain() => Reported("\"a\" + s + \"b\" + n + \"c\"", "$\"a{s}b{n}c\"");

    [Fact]
    public Task AdjacentLiteralsMerge() => Reported("\"a\" + \"b\" + s", "$\"ab{s}\"");

    [Fact]
    public Task NonStringOperand() => Reported("\"n = \" + n", "$\"n = {n}\"");

    [Fact]
    public Task NumericAdditionOnTheLeftIsOneOperand() => Reported("1 + n + \"a\"", "$\"{1 + n}a\"");

    [Fact]
    public Task BracesOfTheLiteralAreDoubled() => Reported("\"{\" + s + \"}\"", "$\"{{{s}}}\"");

    [Fact]
    public Task EscapesAreKept() => Reported("\"a\\n\\\"\" + s", "$\"a\\n\\\"{s}\"");

    [Fact]
    public Task ConditionalOperandGetsParentheses() => Reported("\"a\" + (c ? s : \"b\")", "$\"a{(c ? s : \"b\")}\"");

    [Fact]
    public Task MemberAndCallOperands() => Reported("\"a\" + Name.Trim() + a.Length", "$\"a{Name.Trim()}{a.Length}\"");

    [Fact]
    public Task NullCoalescingOperand() => Reported("\"a\" + (s ?? \"b\")", "$\"a{(s ?? \"b\")}\"");

    [Fact]
    public Task GenericCallWithCommaGetsParentheses() => FixAsync(
        "class C\n{\n    static int F<A, B>() => 0;\n\n    string M() => {|NLR0021:\"a\" + F<int, string>()|};\n}\n",
        "class C\n{\n    static int F<A, B>() => 0;\n\n    string M() => $\"a{(F<int, string>())}\";\n}\n");

    [Fact]
    public Task CompoundAssignmentRightSide() => FixAsync(
        Class("        t = \"\";\n        t += {|NLR0021:\"a\" + n|};\n"),
        Class("        t = \"\";\n        t += $\"a{n}\";\n"));

    [Fact]
    public Task ExpressionBody() => FixAsync(
        "class C\n{\n    string M(string s) => {|NLR0021:\"a\" + s|};\n}\n",
        "class C\n{\n    string M(string s) => $\"a{s}\";\n}\n");

    [Fact]
    public Task Argument() => FixAsync(
        "class C\n{\n    void M(string s) => N({|NLR0021:s + \"b\"|});\n\n    void N(string s) { }\n}\n",
        "class C\n{\n    void M(string s) => N($\"{s}b\");\n\n    void N(string s) { }\n}\n");

    [Fact]
    public Task TriviaAndIndentationAreKept() => FixAsync(
        Class("        // before\n        t = {|NLR0021:\"a\" + s|}; // after\n"),
        Class("        // before\n        t = $\"a{s}\"; // after\n"));

    [Fact]
    public Task FixAll() => FixAsync(
        Class("        t = {|NLR0021:\"a\" + s|};\n        t = {|NLR0021:n + \"b\"|};\n        t = {|NLR0021:\"c\" + o|};\n"),
        Class("        t = $\"a{s}\";\n        t = $\"{n}b\";\n        t = $\"c{o}\";\n"));

    // The outer chain waits: it is reported once the inner one is fixed.
    [Fact]
    public Task InnerChainIsFixedFirst() => FixAsync(
        Class("        t = \"a\" + ({|NLR0021:s + \"b\"|}) + n;\n"),
        Class("        t = \"a\" + ($\"{s}b\") + n;\n"));

    [Fact]
    public Task CSharp6IsEnough() => Reported("\"a\" + s", "$\"a{s}\"", LanguageVersion.CSharp6);

    [Fact]
    public Task LambdaIsNotAnExpressionTree() => FixAsync(
        Class("        Func<string, string> f = x => {|NLR0021:\"a\" + x|};\n"),
        Class("        Func<string, string> f = x => $\"a{x}\";\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task TwoVariablesAreNotReported() => NotReported("s + s");

    [Fact]
    public Task OnlyLiteralsAreNotReported() => NotReported("\"a\" + \"b\"");

    [Fact]
    public Task ConstantIsNotReported() => NotReported("\"a\" + K");

    [Fact]
    public Task ConstDeclarationIsNotReported() => NoDiagnosticAsync(
        "class C\n{\n    const string A = \"a\";\n    const string B = A + \"b\";\n    [System.Obsolete(A + \"c\")] void M() { }\n}\n");

    [Fact]
    public Task NumericAdditionIsNotReported() => NotReported("(1 + n).ToString()");

    [Fact]
    public Task ConstantWithNumberIsNotReported() => NotReported("\"a\" + 1");

    [Fact]
    public Task VerbatimLiteralIsNotReported() => NotReported("@\"a\" + s");

    [Fact]
    public Task RawLiteralIsNotReported() => NotReported("\"\"\"a\"\"\" + s");

    [Fact]
    public Task InterpolatedOperandIsNotReported() => NotReported("\"a\" + $\"{s}\"");

    [Fact]
    public Task DynamicIsNotReported() => NoDiagnosticAsync(
        "class C\n{\n    object M(dynamic d) => \"a\" + d;\n}\n");

    [Fact]
    public Task UserDefinedOperatorIsNotReported() => NoDiagnosticAsync(
        "class V\n{\n    public static V operator +(string a, V b) => b;\n}\n\nclass C\n{\n    V M(V v) => \"a\" + v;\n}\n");

    [Fact]
    public Task BracesInAnOperandAreNotReported() => NotReported("\"a\" + new[] { 1 }.Length");

    [Fact]
    public Task CommentIsNotReported() => NotReported("\"a\" /* c */ + s");

    [Fact]
    public Task SeveralLinesAreNotReported() => NoDiagnosticAsync(
        Class("        t = \"a\" +\n            s;\n"));

    [Fact]
    public Task CharLiteralAloneIsNotAStringLiteral() => NotReported("s + ':'");

    [Fact]
    public Task ExpressionTreeIsNotReported() => NoDiagnosticAsync(
        Class("        Expression<Func<string, string>> e = x => \"a\" + x;\n"));

    [Fact]
    public Task QueryIsNotReported() => NoDiagnosticAsync(
        Class("        var q = from x in a select \"a\" + x;\n"));

    [Fact]
    public Task OlderLanguageIsNotReported() => NotReported("\"a\" + s", LanguageVersion.CSharp5);
}
