using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.BooleanComparison;
using Neolinter.Analyzers.ElseAfterExit;
using Neolinter.Analyzers.ExplicitBoolean;
using Neolinter.CodeFixes.ExplicitBoolean;

namespace Neolinter.Tests.ExplicitBoolean;

/// <summary>NLR0010 tests. Several diagnostics in one source also exercise fix all.</summary>
public class ExplicitBooleanTests
{
    private sealed class Test : CSharpCodeFixTest<ExplicitBooleanAnalyzer, ExplicitBooleanCodeFixProvider, DefaultVerifier>
    {
        private readonly bool _otherRules;

        public Test(string source, string fixedSource, bool otherRules = false)
        {
            _otherRules = otherRules;
            TestCode = source;
            FixedCode = fixedSource;
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        }

        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
            _otherRules
                ? base.GetDiagnosticAnalyzers().Append(new ElseAfterExitAnalyzer()).Append(new BooleanComparisonAnalyzer())
                : base.GetDiagnosticAnalyzers();
    }

    private static Task FixAsync(string source, string fixedSource, bool otherRules = false) => new Test(source, fixedSource, otherRules).RunAsync();

    private static Task NoDiagnosticAsync(string source) => new Test(source, source).RunAsync();

    private static string Method(string body) =>
        $$"""
        class C
        {
            bool Run() => true;
            bool Flag { get; set; }

            bool M(bool c, bool d, int n, bool? nb, object o)
            {
        {{body}}    }
        }
        """;

    // --- Conditional expression --------------------------------------------

    [Theory]
    [InlineData("c", "true : false", "c")]
    [InlineData("c", "false : true", "!c")]
    [InlineData("c && d", "true : false", "c && d")]
    [InlineData("c && d", "false : true", "!(c && d)")]
    [InlineData("!c", "false : true", "c")]
    [InlineData("n > 3", "false : true", "!(n > 3)")]
    [InlineData("Run()", "false : true", "!Run()")]
    [InlineData("o is int", "true : false", "o is int")]
    [InlineData("nb ?? false", "true : false", "nb ?? false")]
    public Task Ternary(string condition, string branches, string expected) => FixAsync(
        Method($"        return {condition} {{|NLR0010:?|}} {branches};\n"),
        Method($"        return {expected};\n"));

    [Fact]
    public Task TernaryAsAnArgumentAndInAnAssignment() => FixAsync(
        Method("        var x = c {|NLR0010:?|} true : false;\n        Flag = d {|NLR0010:?|} false : true;\n        return x;\n"),
        Method("        var x = c;\n        Flag = !d;\n        return x;\n"));

    [Fact]
    public Task TernaryInsideParentheses() => FixAsync(
        Method("        return c && (d {|NLR0010:?|} true : false);\n"),
        Method("        return c && (d);\n"));

    [Fact]
    public Task TernaryKeepsSurroundingComments() => FixAsync(
        Method("        return /* a */ c {|NLR0010:?|} true : false; // b\n"),
        Method("        return /* a */ c; // b\n"));

    [Fact]
    public Task TernaryAssignedToANullableBool() => FixAsync(
        Method("        bool? x = c {|NLR0010:?|} true : false;\n        return x == true;\n"),
        Method("        bool? x = c;\n        return x == true;\n"));

    [Theory]
    [InlineData("c ? true : true")]
    [InlineData("c ? false : false")]
    [InlineData("c ? d : false")]
    [InlineData("c ? true : d")]
    [InlineData("c ? (true) : false")]
    public Task TernaryNotReported(string expression) => NoDiagnosticAsync(Method($"        var x = {expression};\n        return x;\n"));

    [Fact]
    public Task TernaryOnADynamicConditionIsNotReported() => NoDiagnosticAsync(
        Method("        dynamic dyn = c;\n        return dyn ? true : false;\n"));

    [Fact]
    public Task TernaryWithACommentInsideIsNotReported() => NoDiagnosticAsync(
        Method("        return c ? /* yes */ true : false;\n"));

    [Fact]
    public Task TernaryWithADirectiveInsideIsNotReported() => NoDiagnosticAsync(
        Method("        return c ?\n#if DEBUG\n            true\n#else\n            true\n#endif\n            : false;\n"));

    // --- if / else ---------------------------------------------------------

    [Fact]
    public Task IfElseOnOneLine() => FixAsync(
        Method("        {|NLR0010:if|} (c) return true; else return false;\n"),
        Method("        return c;\n"));

    [Fact]
    public Task IfElseReversed() => FixAsync(
        Method("        {|NLR0010:if|} (c) return false; else return true;\n"),
        Method("        return !c;\n"));

    [Fact]
    public Task IfElseWithBraces() => FixAsync(
        Method("""
                    {|NLR0010:if|} (c)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }

            """),
        Method("        return c;\n"));

    [Fact]
    public Task IfElseKeepsALeadingCommentAndTheIndentation() => FixAsync(
        Method("        // explain\n        {|NLR0010:if|} (c && d)\n            return true;\n        else\n            return false;\n"),
        Method("        // explain\n        return c && d;\n"));

    [Fact]
    public Task IfElseKeepsATrailingComment() => FixAsync(
        Method("        {|NLR0010:if|} (c) return false; else return true; // inverted\n"),
        Method("        return !c; // inverted\n"));

    [Fact]
    public Task IfElseAfterAnElseIf() => FixAsync(
        Method("        if (d) return true;\n        else {|NLR0010:if|} (c) return true; else return false;\n"),
        Method("        if (d) return true;\n        else return c;\n"));

    [Fact]
    public Task IfElseKeepsWhatFollowsTheIfInAnotherBranch() => FixAsync(
        Method("        if (d)\n        {\n            {|NLR0010:if|} (c) return true; else return false;\n        }\n\n        return false;\n"),
        Method("        if (d)\n        {\n            return c;\n        }\n\n        return false;\n"));

    [Fact]
    public Task IfElseWithAPatternVariableInTheCondition() => FixAsync(
        Method("        {|NLR0010:if|} (o is int i) return true; else return false;\n"),
        Method("        return o is int i;\n"));

    [Fact]
    public Task IfElseOnTabsAndCrLf() => FixAsync(
        Method("\t\t{|NLR0010:if|} (c)\r\n\t\t\treturn true;\r\n\t\telse\r\n\t\t\treturn false;\r\n"),
        Method("\t\treturn c;\r\n"));

    // --- if followed by a return -------------------------------------------

    [Fact]
    public Task IfThenFinalReturn() => FixAsync(
        Method("        {|NLR0010:if|} (c) return true;\n        return false;\n"),
        Method("        return c;\n"));

    [Fact]
    public Task IfThenFinalReturnReversed() => FixAsync(
        Method("        {|NLR0010:if|} (n > 3)\n        {\n            return false;\n        }\n\n        return true;\n"),
        Method("        return !(n > 3);\n"));

    [Fact]
    public Task IfThenFinalReturnKeepsTheCommentBeforeAndAfter() => FixAsync(
        Method("        Run();\n        // check\n        {|NLR0010:if|} (c) return true;\n        return false; // done\n"),
        Method("        Run();\n        // check\n        return c; // done\n"));

    [Fact]
    public Task IfThenFinalReturnInASwitchSection() => FixAsync(
        Method("        switch (n)\n        {\n            case 1:\n                {|NLR0010:if|} (c) return true;\n                return false;\n            default:\n                return d;\n        }\n"),
        Method("        switch (n)\n        {\n            case 1:\n                return c;\n            default:\n                return d;\n        }\n"));

    [Fact]
    public Task IfThenFinalReturnWithCodeAfterIsKept() => FixAsync(
        Method("        {|NLR0010:if|} (c) return true;\n        return false;\n        Run();\n"),
        Method("        return c;\n        Run();\n"));

    // --- Fix all and interaction with other rules -----------------------------

    [Fact]
    public Task FixAllInOneMethod() => FixAsync(
        Method("        if (d) return n > 3 {|NLR0010:?|} true : false;\n        {|NLR0010:if|} (c) return true; else return false;\n"),
        Method("        if (d) return n > 3;\n        return c;\n"));

    [Fact]
    public Task NestedTernariesFixAll() => FixAsync(
        Method("        return (c {|NLR0010:?|} true : false) {|NLR0010:?|} true : false;\n"),
        Method("        return (c);\n"));

    [Fact]
    public Task ComparisonWithTrueInsideTheCondition() => FixAsync(
        Method("        return {|NLR0005:c == true|} {|NLR0010:?|} true : false;\n"),
        Method("        return {|NLR0005:c == true|};\n"),
        otherRules: true);

    // --- Not reported ------------------------------------------------------

    [Theory]
    [InlineData("if (c) return true; else return true;")]
    [InlineData("if (c) return false; return false;")]
    [InlineData("if (c) return true; else return d;")]
    [InlineData("if (c) return d; return false;")]
    [InlineData("if (c) { Run(); return true; } else return false;")]
    [InlineData("if (c) { return true; } else { Run(); return false; }")]
    [InlineData("if (c) return true; Run(); return false;")]
    [InlineData("if (c) return (true); else return false;")]
    [InlineData("if (c) { } else return false;")]
    public Task OtherShapesAreNotReported(string statements) => NoDiagnosticAsync(
        Method($"        {statements}\n        return d;\n"));

    [Fact]
    public Task IfWithACommentInsideIsNotReported() => NoDiagnosticAsync(
        Method("        if (c)\n        {\n            // yes\n            return true;\n        }\n        else return false;\n"));

    [Fact]
    public Task IfThenReturnWithACommentBetweenIsNotReported() => NoDiagnosticAsync(
        Method("        if (c) return true;\n        // otherwise\n        return false;\n"));

    [Fact]
    public Task IfWithADirectiveInsideIsNotReported() => NoDiagnosticAsync(
        Method("        if (c) return true;\n#if DEBUG\n        else return false;\n#else\n        else return false;\n#endif\n"));

    [Fact]
    public Task IfThenReturnAcrossALabelIsNotReported() => NoDiagnosticAsync(
        Method("        if (c) return true;\n    End:\n        return false;\n"));

    // --- NLR0009 stays silent on the shape this rule takes ---------------------

    [Fact]
    public Task Nlr0009StaysSilentOnReturnTrueElseReturnFalse() => FixAsync(
        Method("        {|NLR0010:if|} (c) { return true; } else { return false; }\n"),
        Method("        return c;\n"),
        otherRules: true);
}
