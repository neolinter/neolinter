using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.UselessCatch;
using Neolinter.CodeFixes.UselessCatch;

namespace Neolinter.Tests.UselessCatch;

/// <summary>NLR0037 tests. Several diagnostics in one source also exercise fix all.</summary>
public class UselessCatchTests
{
    private sealed class Test : CSharpCodeFixTest<UselessCatchAnalyzer, UselessCatchCodeFixProvider, DefaultVerifier>
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
        "using System;\nusing System.IO;\n\nclass C\n{\n    bool flag;\n\n    void A() { }\n\n    void M()\n    {\n" + body + "    }\n}\n";

    private const string Try = "        try\n        {\n            A();\n        }\n";

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task OnlyCatchWithoutFinally_TryReplacedByBlock() => FixAsync(
        Class(Try + "        {|NLR0037:catch|} (IOException)\n        {\n            throw;\n        }\n"),
        Class("        {\n            A();\n        }\n"));

    [Fact]
    public Task GeneralCatch() => FixAsync(
        Class(Try + "        {|NLR0037:catch|}\n        {\n            throw;\n        }\n"),
        Class("        {\n            A();\n        }\n"));

    [Fact]
    public Task OnlyCatchWithFinally_FinallyKept() => FixAsync(
        Class(Try + "        {|NLR0037:catch|} (Exception e)\n        {\n            throw;\n        }\n        finally\n        {\n            A();\n        }\n"),
        Class(Try + "        finally\n        {\n            A();\n        }\n"));

    [Fact]
    public Task LastClauseAfterOtherHandler() => FixAsync(
        Class(Try + "        catch (IOException)\n        {\n            A();\n        }\n        {|NLR0037:catch|} (ArgumentException)\n        {\n            throw;\n        }\n"),
        Class(Try + "        catch (IOException)\n        {\n            A();\n        }\n"));

    [Fact]
    public Task FirstClauseOfUnrelatedTypes() => FixAsync(
        Class(Try + "        {|NLR0037:catch|} (ArgumentException)\n        {\n            throw;\n        }\n        catch (IOException)\n        {\n            A();\n        }\n"),
        Class(Try + "        catch (IOException)\n        {\n            A();\n        }\n"));

    [Fact]
    public Task CommentInsideIsRemovedWithTheClause() => FixAsync(
        Class("        // guard\n" + Try + "        {|NLR0037:catch|} (IOException)\n        {\n            // rethrow\n            throw;\n        }\n"),
        Class("        // guard\n        {\n            A();\n        }\n"));

    [Fact]
    public Task TryAsEmbeddedStatement() => FixAsync(
        Class("        if (flag)\n            try\n            {\n                A();\n            }\n            {|NLR0037:catch|}\n            {\n                throw;\n            }\n"),
        Class("        if (flag)\n            {\n                A();\n            }\n"));

    // --- Fix all ---------------------------------------------------------------------

    [Fact]
    public Task FixAll_TwoUselessClauses_TryBecomesBlock() => FixAsync(
        Class(Try + "        {|NLR0037:catch|} (ArgumentException)\n        {\n            throw;\n        }\n        {|NLR0037:catch|} (IOException)\n        {\n            throw;\n        }\n"),
        Class("        {\n            A();\n        }\n"));

    [Fact]
    public Task FixAll_TwoUselessClausesWithFinally() => FixAsync(
        Class(Try + "        {|NLR0037:catch|} (ArgumentException)\n        {\n            throw;\n        }\n        {|NLR0037:catch|} (IOException)\n        {\n            throw;\n        }\n        finally\n        {\n            A();\n        }\n"),
        Class(Try + "        finally\n        {\n            A();\n        }\n"));

    [Fact]
    public Task FixAll_SeveralTries() => FixAsync(
        Class("        try { A(); }\n        {|NLR0037:catch|} { throw; }\n        try { A(); }\n        catch (IOException) { A(); }\n        {|NLR0037:catch|} (ArgumentException) { throw; }\n"),
        Class("        { A(); }\n        try { A(); }\n        catch (IOException) { A(); }\n"));

    [Fact]
    public Task FixAll_NestedTries() => FixAsync(
        Class("        try\n        {\n            try { A(); }\n            {|NLR0037:catch|} (ArgumentException) { throw; }\n        }\n        {|NLR0037:catch|} (IOException) { throw; }\n"),
        Class("        {\n            { A(); }\n        }\n"));

    // --- Not reported ----------------------------------------------------------------

    [Fact]
    public Task NotReported_ShieldsBroaderClause() => NoDiagnosticAsync(
        Class(Try + "        catch (ArgumentException)\n        {\n            throw;\n        }\n        catch (Exception)\n        {\n            A();\n        }\n"));

    [Fact]
    public Task NotReported_ShieldsGeneralClause() => NoDiagnosticAsync(
        Class(Try + "        catch (IOException)\n        {\n            throw;\n        }\n        catch\n        {\n            A();\n        }\n"));

    [Fact]
    public Task NotReported_ShieldsBroaderClauseWithFilter() => NoDiagnosticAsync(
        Class(Try + "        catch (IOException)\n        {\n            throw;\n        }\n        catch (Exception) when (flag)\n        {\n            A();\n        }\n"));

    [Fact]
    public Task NotReported_Filter() => NoDiagnosticAsync(
        Class(Try + "        catch (IOException) when (flag)\n        {\n            throw;\n        }\n"));

    [Fact]
    public Task NotReported_OtherStatements() => NoDiagnosticAsync(
        Class(Try + "        catch (IOException)\n        {\n            A();\n            throw;\n        }\n"));

    [Fact]
    public Task NotReported_ThrowEx() => NoDiagnosticAsync(
        Class(Try + "        catch (IOException e)\n        {\n            throw e;\n        }\n"));

    [Fact]
    public Task NotReported_ThrowNewException() => NoDiagnosticAsync(
        Class(Try + "        catch (IOException e)\n        {\n            throw new InvalidOperationException(\"x\", e);\n        }\n"));

    [Fact]
    public Task NotReported_NestedThrow() => NoDiagnosticAsync(
        Class(Try + "        catch (IOException)\n        {\n            if (flag) { throw; }\n        }\n"));

    [Fact]
    public Task NotReported_EmptyCatch() => NoDiagnosticAsync(
        Class(Try + "        catch (IOException)\n        {\n        }\n"));
}
