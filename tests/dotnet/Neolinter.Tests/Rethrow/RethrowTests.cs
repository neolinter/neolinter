using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.Rethrow;
using Neolinter.CodeFixes.Rethrow;

namespace Neolinter.Tests.Rethrow;

/// <summary>NLR0023 tests. Several diagnostics in one source also exercise fix all.</summary>
public class RethrowTests
{
    private sealed class Test : CSharpCodeFixTest<RethrowAnalyzer, RethrowCodeFixProvider, DefaultVerifier>
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
        "using System;\nusing System.Threading.Tasks;\n\nclass C\n{\n    void Work() { }\n\n    void M()\n    {\n" + body + "    }\n}\n";

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task SimpleCatch() => FixAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            {|NLR0023:throw e;|}\n        }\n"),
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            throw;\n        }\n"));

    [Fact]
    public Task AfterOtherStatements() => FixAsync(
        Class("        try { Work(); }\n        catch (InvalidOperationException ex)\n        {\n            Console.WriteLine(ex);\n            {|NLR0023:throw ex;|}\n        }\n"),
        Class("        try { Work(); }\n        catch (InvalidOperationException ex)\n        {\n            Console.WriteLine(ex);\n            throw;\n        }\n"));

    [Fact]
    public Task WithExceptionFilter() => FixAsync(
        Class("        try { Work(); }\n        catch (Exception e) when (e.Message.Length > 0)\n        {\n            {|NLR0023:throw e;|}\n        }\n"),
        Class("        try { Work(); }\n        catch (Exception e) when (e.Message.Length > 0)\n        {\n            throw;\n        }\n"));

    [Fact]
    public Task InsideIfInCatch() => FixAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            if (e.Message == \"\")\n            {\n                {|NLR0023:throw e;|}\n            }\n        }\n"),
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            if (e.Message == \"\")\n            {\n                throw;\n            }\n        }\n"));

    [Fact]
    public Task InsideTryBlockNestedInCatch() => FixAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            try { {|NLR0023:throw e;|} }\n            catch (ArgumentException) { }\n        }\n"),
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            try { throw; }\n            catch (ArgumentException) { }\n        }\n"));

    [Fact]
    public Task InsideAsyncMethod() => FixAsync(
        "using System;\nusing System.Threading.Tasks;\n\nclass C\n{\n    async Task M()\n    {\n        try { await Task.Delay(1); }\n        catch (Exception e)\n        {\n            {|NLR0023:throw e;|}\n        }\n    }\n}\n",
        "using System;\nusing System.Threading.Tasks;\n\nclass C\n{\n    async Task M()\n    {\n        try { await Task.Delay(1); }\n        catch (Exception e)\n        {\n            throw;\n        }\n    }\n}\n");

    [Fact]
    public Task CommentsAreKept() => FixAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            // keep the cause\n            {|NLR0023:throw /* why */ e;|} // end\n        }\n"),
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            // keep the cause\n            throw /* why */; // end\n        }\n"));

    [Fact]
    public Task TwoCatchClausesFixAll() => FixAsync(
        Class("        try { Work(); }\n        catch (ArgumentException a)\n        {\n            {|NLR0023:throw a;|}\n        }\n        catch (Exception e)\n        {\n            {|NLR0023:throw e;|}\n        }\n"),
        Class("        try { Work(); }\n        catch (ArgumentException a)\n        {\n            throw;\n        }\n        catch (Exception e)\n        {\n            throw;\n        }\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task NotReportedBareRethrow() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception)\n        {\n            throw;\n        }\n"));

    [Fact]
    public Task NotReportedWrappedException() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            throw new InvalidOperationException(\"x\", e);\n        }\n"));

    [Fact]
    public Task NotReportedOtherException() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception)\n        {\n            var other = new ArgumentException();\n            throw other;\n        }\n"));

    [Fact]
    public Task NotReportedMemberOfException() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (AggregateException e)\n        {\n            throw e.InnerException;\n        }\n"));

    [Fact]
    public Task NotReportedVariableOutsideCatch() => NoDiagnosticAsync(
        Class("        Exception e = new Exception();\n        throw e;\n"));

    [Fact]
    public Task NotReportedParameter() => NoDiagnosticAsync(
        "using System;\n\nclass C\n{\n    void M(Exception e)\n    {\n        throw e;\n    }\n}\n");

    [Fact]
    public Task NotReportedThrowExpression() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            object o = null;\n            var x = o ?? throw e;\n        }\n"));

    [Fact]
    public Task NotReportedInLambda() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            Action a = () => { throw e; };\n        }\n"));

    [Fact]
    public Task NotReportedInAnonymousMethod() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            Action a = delegate { throw e; };\n        }\n"));

    [Fact]
    public Task NotReportedInLocalFunction() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            void Local() { throw e; }\n            Local();\n        }\n"));

    [Fact]
    public Task NotReportedInFinallyWithinCatch() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            try { Work(); }\n            finally { throw e; }\n        }\n"));

    [Fact]
    public Task NotReportedInNestedCatch() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            try { Work(); }\n            catch (ArgumentException)\n            {\n                throw e;\n            }\n        }\n"));

    [Fact]
    public Task NotReportedNestedCatchSameName() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception)\n        {\n            try { Work(); }\n            catch (ArgumentException e)\n            {\n                try { Work(); }\n                catch (InvalidOperationException)\n                {\n                    throw e;\n                }\n            }\n        }\n"));

    [Fact]
    public Task NotReportedReassigned() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            e = new ArgumentException();\n            throw e;\n        }\n"));

    [Fact]
    public Task NotReportedReassignedAfterThrowSite() => NoDiagnosticAsync(
        Class("        try { Work(); }\n        catch (Exception e)\n        {\n            if (e.Message == \"\")\n            {\n                throw e;\n            }\n\n            e = null;\n        }\n"));
}
