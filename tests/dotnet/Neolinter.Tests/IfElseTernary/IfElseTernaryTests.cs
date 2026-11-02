using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.ElseAfterExit;
using Neolinter.Analyzers.ExplicitBoolean;
using Neolinter.Analyzers.IfElseTernary;
using Neolinter.Analyzers.NestedTernary;
using Neolinter.CodeFixes.IfElseTernary;

namespace Neolinter.Tests.IfElseTernary;

/// <summary>NLR0020 tests. Several diagnostics in one source also exercise fix all.</summary>
public class IfElseTernaryTests
{
    private sealed class Test : CSharpCodeFixTest<IfElseTernaryAnalyzer, IfElseTernaryCodeFixProvider, DefaultVerifier>
    {
        private readonly bool _otherRules;

        public Test(string source, string fixedSource, LanguageVersion version, bool otherRules)
        {
            _otherRules = otherRules;
            TestCode = source;
            FixedCode = fixedSource;
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
            SolutionTransforms.Add((solution, projectId) =>
            {
                var options = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
                return solution.WithProjectParseOptions(projectId, options.WithLanguageVersion(version));
            });
        }

        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
            _otherRules
                ? base.GetDiagnosticAnalyzers().Append(new NestedTernaryAnalyzer()).Append(new ElseAfterExitAnalyzer()).Append(new ExplicitBooleanAnalyzer())
                : base.GetDiagnosticAnalyzers();
    }

    private static Task FixAsync(string source, string fixedSource, LanguageVersion version = LanguageVersion.Latest, bool otherRules = false) =>
        new Test(source, fixedSource, version, otherRules).RunAsync();

    private static Task NoDiagnosticAsync(string source, LanguageVersion version = LanguageVersion.Latest, bool otherRules = false) =>
        new Test(source, source, version, otherRules).RunAsync();

    private static string Class(string members) =>
        "using System;\n\nclass C\n{\n    int _field;\n    string Name { get; set; } = \"\";\n    C _other = null!;\n    int[] _array = new int[3];\n\n" + members + "}\n";

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task AssignmentWithoutBraces() => FixAsync(
        Class("    void M(bool c)\n    {\n        int x;\n        {|NLR0020:if|} (c)\n            x = 1;\n        else\n            x = 2;\n\n        Console.WriteLine(x);\n    }\n"),
        Class("    void M(bool c)\n    {\n        int x;\n        x = c ? 1 : 2;\n\n        Console.WriteLine(x);\n    }\n"));

    [Fact]
    public Task AssignmentWithBraces() => FixAsync(
        Class("    void M(bool c)\n    {\n        {|NLR0020:if|} (c) { _field = 1; } else { _field = 2; }\n    }\n"),
        Class("    void M(bool c)\n    {\n        _field = c ? 1 : 2;\n    }\n"));

    [Fact]
    public Task ThisMemberAndMemberChain() => FixAsync(
        Class("    void M(bool c)\n    {\n        {|NLR0020:if|} (c) this.Name = \"a\"; else this.Name = \"b\";\n        {|NLR0020:if|} (c) _other.Name = \"a\"; else _other.Name = \"b\";\n    }\n"),
        Class("    void M(bool c)\n    {\n        this.Name = c ? \"a\" : \"b\";\n        _other.Name = c ? \"a\" : \"b\";\n    }\n"));

    [Fact]
    public Task ReturnWithElse() => FixAsync(
        Class("    string M(bool c)\n    {\n        {|NLR0020:if|} (c)\n        {\n            return \"a\";\n        }\n        else\n        {\n            return \"b\";\n        }\n    }\n"),
        Class("    string M(bool c)\n    {\n        return c ? \"a\" : \"b\";\n    }\n"));

    [Fact]
    public Task ReturnWithoutElse() => FixAsync(
        Class("    string M(bool c)\n    {\n        {|NLR0020:if|} (c)\n            return \"a\";\n        return \"b\";\n    }\n"),
        Class("    string M(bool c)\n    {\n        return c ? \"a\" : \"b\";\n    }\n"));

    [Fact]
    public Task ReturnWithoutElseInASwitchSection() => FixAsync(
        Class("    string M(int n, bool c)\n    {\n        switch (n)\n        {\n            case 1:\n                {|NLR0020:if|} (c) return \"a\";\n                return \"b\";\n            default:\n                return \"z\";\n        }\n    }\n"),
        Class("    string M(int n, bool c)\n    {\n        switch (n)\n        {\n            case 1:\n                return c ? \"a\" : \"b\";\n            default:\n                return \"z\";\n        }\n    }\n"));

    [Fact]
    public Task CodeAfterTheReturnStaysAndIndentationIsKept() => FixAsync(
        Class("    string M(bool c)\n    {\n        if (c)\n        {\n            {|NLR0020:if|} (_field > 1) return \"a\";\n            return \"b\";\n        }\n\n        return \"c\";\n    }\n"),
        Class("    string M(bool c)\n    {\n        if (c)\n        {\n            return _field > 1 ? \"a\" : \"b\";\n        }\n\n        return \"c\";\n    }\n"));

    [Fact]
    public Task TrailingCommentAfterTheStatementIsKept() => FixAsync(
        Class("    string M(bool c)\n    {\n        {|NLR0020:if|} (c) return \"a\"; else return \"b\"; // done\n    }\n"),
        Class("    string M(bool c)\n    {\n        return c ? \"a\" : \"b\"; // done\n    }\n"));

    [Fact]
    public Task LeadingCommentBeforeTheIfIsKept() => FixAsync(
        Class("    string M(bool c)\n    {\n        // pick one\n        {|NLR0020:if|} (c) return \"a\"; else return \"b\";\n    }\n"),
        Class("    string M(bool c)\n    {\n        // pick one\n        return c ? \"a\" : \"b\";\n    }\n"));

    [Fact]
    public Task NullBranchWithAReferenceType() => FixAsync(
        Class("    string? M(bool c, string s)\n    {\n        {|NLR0020:if|} (c) return s; else return null;\n    }\n"),
        Class("    string? M(bool c, string s)\n    {\n        return c ? s : null;\n    }\n"));

    [Fact]
    public Task NullableValueTypeNeedsTargetTyping() => FixAsync(
        Class("    int? M(bool c)\n    {\n        {|NLR0020:if|} (c) return 1; else return null;\n    }\n"),
        Class("    int? M(bool c)\n    {\n        return c ? 1 : null;\n    }\n"));

    [Fact]
    public Task BranchesOfAnotherTypeThanTheTargetWithACommonType() => FixAsync(
        Class("    double M(bool c)\n    {\n        {|NLR0020:if|} (c) return 1; else return 2.5;\n    }\n"),
        Class("    double M(bool c)\n    {\n        return c ? 1 : 2.5;\n    }\n"));

    [Fact]
    public Task OneBooleanLiteralBranchIsReported() => FixAsync(
        Class("    bool M(bool c, bool d)\n    {\n        {|NLR0020:if|} (c) return d; else return false;\n    }\n"),
        Class("    bool M(bool c, bool d)\n    {\n        return c ? d : false;\n    }\n"));

    [Fact]
    public Task AwaitInOneBranch() => FixAsync(
        "using System.Threading.Tasks;\n\nclass C\n{\n    async Task<int> M(bool c, Task<int> t)\n    {\n        {|NLR0020:if|} (c) return await t; else return 0;\n    }\n}\n",
        "using System.Threading.Tasks;\n\nclass C\n{\n    async Task<int> M(bool c, Task<int> t)\n    {\n        return c ? await t : 0;\n    }\n}\n");

    [Fact]
    public Task OutVariableInAReturnCondition() => FixAsync(
        Class("    int M(string s)\n    {\n        {|NLR0020:if|} (int.TryParse(s, out var n)) return n; else return 0;\n    }\n"),
        Class("    int M(string s)\n    {\n        return int.TryParse(s, out var n) ? n : 0;\n    }\n"));

    [Fact]
    public Task SeveralIfsAreAllFixed() => FixAsync(
        Class("    string M(bool c, bool d)\n    {\n        string s;\n        {|NLR0020:if|} (c) s = \"a\"; else s = \"b\";\n        {|NLR0020:if|} (d) return s; else return \"z\";\n    }\n"),
        Class("    string M(bool c, bool d)\n    {\n        string s;\n        s = c ? \"a\" : \"b\";\n        return d ? s : \"z\";\n    }\n"));

    [Fact]
    public Task BranchOfExactlyTheLimitIsReported() => FixAsync(
        Class("    string M(bool c)\n    {\n        {|NLR0020:if|} (c) return \"" + new string('a', 58) + "\"; else return \"b\";\n    }\n"),
        Class("    string M(bool c)\n    {\n        return c ? \"" + new string('a', 58) + "\" : \"b\";\n    }\n"));

    [Fact]
    public Task PatternVariableInAReturnCondition() => FixAsync(
        Class("    string M(object o)\n    {\n        {|NLR0020:if|} (o is string s) return s; else return \"\";\n    }\n"),
        Class("    string M(object o)\n    {\n        return o is string s ? s : \"\";\n    }\n"));

    [Fact]
    public Task TypeTestCondition() => FixAsync(
        Class("    string M(object o)\n    {\n        {|NLR0020:if|} (o is string) return \"s\"; else return \"o\";\n    }\n"),
        Class("    string M(object o)\n    {\n        return o is string ? \"s\" : \"o\";\n    }\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task BranchOverTheLimitIsNotReported() => NoDiagnosticAsync(
        Class("    string M(bool c)\n    {\n        if (c) return \"" + new string('a', 59) + "\"; else return \"b\";\n    }\n"));

    [Fact]
    public Task MultiLineBranchIsNotReported() => NoDiagnosticAsync(
        Class("    string M(bool c)\n    {\n        if (c) return \"a\";\n        return string.Join(\n            \",\", \"b\");\n    }\n"));

    [Fact]
    public Task ElseIfIsNotReported() => NoDiagnosticAsync(
        Class("    string M(int n)\n    {\n        if (n < 0) return \"neg\";\n        else if (n == 0) return \"zero\";\n        else return \"pos\";\n    }\n"));

    [Fact]
    public Task CompoundAssignmentIsNotReported() => NoDiagnosticAsync(
        Class("    void M(bool c)\n    {\n        if (c) _field += 1; else _field += 2;\n    }\n"));

    [Fact]
    public Task DifferentTargetsAreNotReported() => NoDiagnosticAsync(
        Class("    void M(bool c)\n    {\n        int a = 0, b = 0;\n        if (c) a = 1; else b = 1;\n        Console.WriteLine(a + b);\n    }\n"));

    [Fact]
    public Task IndexerTargetIsNotReported() => NoDiagnosticAsync(
        Class("    void M(bool c)\n    {\n        if (c) _array[0] = 1; else _array[0] = 2;\n    }\n"));

    [Fact]
    public Task CallInTheTargetIsNotReported() => NoDiagnosticAsync(
        Class("    C Get() => this;\n\n    void M(bool c)\n    {\n        if (c) Get()._field = 1; else Get()._field = 2;\n    }\n"));

    [Fact]
    public Task SeveralStatementsInABranchAreNotReported() => NoDiagnosticAsync(
        Class("    int M(bool c)\n    {\n        if (c) { Console.WriteLine(); return 1; } else { return 2; }\n    }\n"));

    [Fact]
    public Task ReturnAndAssignmentAreNotMixed() => NoDiagnosticAsync(
        Class("    int M(bool c)\n    {\n        int x;\n        if (c) { x = 1; } else { return 2; }\n        return x;\n    }\n"));

    [Fact]
    public Task AssignmentWithoutElseAndNoFollowingReturnIsNotReported() => NoDiagnosticAsync(
        Class("    void M(bool c)\n    {\n        if (c) _field = 1;\n        _field = 2;\n    }\n"));

    [Fact]
    public Task ReturnFollowedByAnotherStatementIsNotReported() => NoDiagnosticAsync(
        Class("    int M(bool c)\n    {\n        if (c) return 1;\n        Console.WriteLine();\n        return 2;\n    }\n"));

    [Fact]
    public Task CommentInsideIsNotReported() => NoDiagnosticAsync(
        Class("    string M(bool c)\n    {\n        if (c) return \"a\"; // usual\n        return \"b\";\n    }\n"));

    [Fact]
    public Task CommentBetweenTheBranchesIsNotReported() => NoDiagnosticAsync(
        Class("    string M(bool c)\n    {\n        if (c) return \"a\";\n        /* otherwise */\n        return \"b\";\n    }\n"));

    [Fact]
    public Task DirectiveIsNotReported() => NoDiagnosticAsync(
        Class("    string M(bool c)\n    {\n        if (c) return \"a\";\n#if DEBUG\n        return \"d\";\n#else\n        return \"b\";\n#endif\n    }\n"));

    [Fact]
    public Task BooleanLiteralsBelongToNlr0010() => NoDiagnosticAsync(
        Class("    bool M(bool c)\n    {\n        if (c) return true; else return false;\n    }\n"));

    [Fact]
    public Task BooleanLiteralAssignmentIsNotReported() => NoDiagnosticAsync(
        Class("    void M(bool c, ref bool r)\n    {\n        bool x;\n        if (c) x = true; else x = false;\n        r = x;\n    }\n"));

    [Fact]
    public Task NestedConditionalInABranchIsNotReported() => NoDiagnosticAsync(
        Class("    int M(bool c, bool d)\n    {\n        if (c) return d ? 1 : 2;\n        return 3;\n    }\n"));

    [Fact]
    public Task ConditionalInsideAnArgumentIsNotReported() => NoDiagnosticAsync(
        Class("    int M(bool c, bool d)\n    {\n        if (c) return Math.Abs(d ? 1 : 2);\n        return 3;\n    }\n"));

    [Fact]
    public Task ConditionalInTheConditionIsNotReported() => NoDiagnosticAsync(
        Class("    int M(bool c, bool d)\n    {\n        if (c ? d : !d) return 1;\n        return 3;\n    }\n"));

    [Fact]
    public Task ConditionalInsideALambdaIsAnotherScope() => FixAsync(
        Class("    int Run(Func<int> f) => f();\n\n    int M(bool c, bool d)\n    {\n        {|NLR0020:if|} (c) return Run(() => d ? 1 : 2);\n        return 3;\n    }\n"),
        Class("    int Run(Func<int> f) => f();\n\n    int M(bool c, bool d)\n    {\n        return c ? Run(() => d ? 1 : 2) : 3;\n    }\n"));

    [Fact]
    public Task LambdaBranchesAreNotReported() => NoDiagnosticAsync(
        Class("    Func<int> M(bool c)\n    {\n        if (c) return () => 1;\n        return () => 3;\n    }\n"));

    [Fact]
    public Task AssignmentBranchIsNotReported() => NoDiagnosticAsync(
        Class("    void M(bool c)\n    {\n        int y;\n        if (c) _field = y = 1; else _field = y = 2;\n    }\n"));

    [Fact]
    public Task RefReturnIsNotReported() => NoDiagnosticAsync(
        Class("    ref int M(bool c, ref int a, ref int b)\n    {\n        if (c) return ref a;\n        return ref b;\n    }\n"));

    [Fact]
    public Task OutVariableInAnAssignmentConditionIsNotReported() => NoDiagnosticAsync(
        Class("    void M(string s)\n    {\n        if (int.TryParse(s, out var n)) _field = n; else _field = 0;\n    }\n"));

    [Fact]
    public Task ValueTypeToNullableIsNotReported() => NoDiagnosticAsync(
        Class("    void M(bool c)\n    {\n        int? x;\n        if (c) x = 1; else x = 2;\n        Console.WriteLine(x);\n    }\n"));

    [Fact]
    public Task ConversionAfterTheNaturalTypeIsNotReported() => NoDiagnosticAsync(
        Class("    double M(bool c)\n    {\n        if (c) return 1; else return 2;\n    }\n"));

    [Fact]
    public Task NoCommonTypeBeforeCSharp9IsNotReported() => NoDiagnosticAsync(
        Class("    int? M(bool c)\n    {\n        if (c) return 1; else return null;\n    }\n"),
        LanguageVersion.CSharp8);

    [Fact]
    public Task CommonTypeBeforeCSharp9IsReported() => FixAsync(
        "class C\n{\n    string M(bool c)\n    {\n        {|NLR0020:if|} (c) return \"a\"; else return \"b\";\n    }\n}\n",
        "class C\n{\n    string M(bool c)\n    {\n        return c ? \"a\" : \"b\";\n    }\n}\n",
        LanguageVersion.CSharp7_3);

    [Fact]
    public Task YieldIsNotReported() => NoDiagnosticAsync(
        "using System.Collections.Generic;\n\nclass C\n{\n    IEnumerable<int> M(bool c)\n    {\n        if (c) yield return 1; else yield return 2;\n    }\n}\n");

    // --- Interplay with other rules ---------------------------------------------------

    // NLR0009 reports the 'else' of the same 'if': both fixes lead to the same place, and fix all of NLR0020 clears both.
    [Fact]
    public Task Nlr0009AlsoReportsTheElseAndTheFixClearsBoth() => FixAsync(
        Class("    string M(bool c)\n    {\n        {|NLR0020:if|} (c) { return \"a\"; } {|NLR0009:else|} { return \"b\"; }\n    }\n"),
        Class("    string M(bool c)\n    {\n        return c ? \"a\" : \"b\";\n    }\n"),
        otherRules: true);

    // NLR0010 keeps 'true' and 'false' for itself: this rule stays silent and leaves the 'if' to NLR0010.
    [Fact]
    public Task Nlr0010KeepsTrueAndFalse() => FixAsync(
        Class("    bool M(bool c)\n    {\n        {|NLR0010:if|} (c) return true; else return false;\n    }\n"),
        Class("    bool M(bool c)\n    {\n        {|NLR0010:if|} (c) return true; else return false;\n    }\n"),
        otherRules: true);

    // NLR0007 has nothing to report after the fix: the result is not a nested conditional.
    [Fact]
    public Task Nlr0007HasNothingToReportAfterTheFix() => FixAsync(
        Class("    string M(bool c, bool d)\n    {\n        {|NLR0020:if|} (c) return \"a\";\n        return \"b\";\n    }\n"),
        Class("    string M(bool c, bool d)\n    {\n        return c ? \"a\" : \"b\";\n    }\n"),
        otherRules: true);
}
