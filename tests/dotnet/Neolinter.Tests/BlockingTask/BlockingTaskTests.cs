using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.BlockingTask;
using Neolinter.CodeFixes.BlockingTask;

namespace Neolinter.Tests.BlockingTask;

/// <summary>NLR0025 tests. Several diagnostics in one source also exercise fix all.</summary>
public class BlockingTaskTests
{
    private sealed class Test : CSharpCodeFixTest<BlockingTaskAnalyzer, BlockingTaskCodeFixProvider, DefaultVerifier>
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

    /// <summary>Reported but not fixable: the code fix must leave the source as it is.</summary>
    private static Task NoFixAsync(string source) => new Test(source, source).RunAsync();

    private const string Header = "using System;\nusing System.Linq;\nusing System.Threading.Tasks;\n\nclass Box\n{\n    public int Result { get; set; }\n}\n\n";

    /// <summary>The body of <c>async Task M(Task&lt;int&gt; t, Task u, ValueTask&lt;int&gt; v, Box box, object o)</c>.</summary>
    private static string Class(string body) =>
        Header + "class C\n{\n    static void Take(int a, int b) { }\n\n    async Task M(Task<int> t, Task u, ValueTask<int> v, Box box, object o)\n    {\n" + body + "    }\n}\n";

    private static string Members(string members) => Header + "class C\n{\n" + members + "}\n";

    // --- Reported, with fix --------------------------------------------------------

    [Fact]
    public Task ResultInitializer() => FixAsync(
        Class("        var x = {|NLR0025:t.Result|};\n"),
        Class("        var x = await t;\n"));

    [Fact]
    public Task ResultReturned() => FixAsync(
        Members("    async Task<int> M(Task<int> t)\n    {\n        return {|NLR0025:t.Result|};\n    }\n"),
        Members("    async Task<int> M(Task<int> t)\n    {\n        return await t;\n    }\n"));

    [Fact]
    public Task ResultInArgumentAndOperator() => FixAsync(
        Class("        Take({|NLR0025:t.Result|}, 1 + {|NLR0025:v.Result|});\n"),
        Class("        Take(await t, 1 + await v);\n"));

    [Fact]
    public Task ValueTaskResult() => FixAsync(
        Class("        var x = {|NLR0025:v.Result|};\n"),
        Class("        var x = await v;\n"));

    [Fact]
    public Task ResultMemberAccessIsParenthesized() => FixAsync(
        Class("        var s = {|NLR0025:t.Result|}.ToString();\n"),
        Class("        var s = (await t).ToString();\n"));

    [Fact]
    public Task ResultOnCallIsParenthesized() => FixAsync(
        Class("        var x = {|NLR0025:Task.Run(() => new[] { 1 }).Result|}[0];\n"),
        Class("        var x = (await Task.Run(() => new[] { 1 }))[0];\n"));

    [Fact]
    public Task WaitStatement() => FixAsync(
        Class("        {|NLR0025:u.Wait()|};\n"),
        Class("        await u;\n"));

    [Fact]
    public Task WaitOnGenericTask() => FixAsync(
        Class("        {|NLR0025:t.Wait()|};\n"),
        Class("        await t;\n"));

    [Fact]
    public Task GetResultValue() => FixAsync(
        Class("        var x = {|NLR0025:t.GetAwaiter().GetResult()|};\n"),
        Class("        var x = await t;\n"));

    [Fact]
    public Task GetResultStatement() => FixAsync(
        Class("        {|NLR0025:u.GetAwaiter().GetResult()|};\n"),
        Class("        await u;\n"));

    [Fact]
    public Task GetResultAfterConfigureAwait() => FixAsync(
        Class("        var x = {|NLR0025:t.ConfigureAwait(false).GetAwaiter().GetResult()|};\n"),
        Class("        var x = await t.ConfigureAwait(false);\n"));

    [Fact]
    public Task ValueTaskGetResult() => FixAsync(
        Class("        var x = {|NLR0025:v.GetAwaiter().GetResult()|};\n"),
        Class("        var x = await v;\n"));

    [Fact]
    public Task AsyncLambda() => FixAsync(
        Class("        Func<Task<int>> f = async () => {|NLR0025:t.Result|};\n"),
        Class("        Func<Task<int>> f = async () => await t;\n"));

    [Fact]
    public Task AsyncLocalFunction() => FixAsync(
        Class("        async Task<int> Local() => {|NLR0025:t.Result|};\n"),
        Class("        async Task<int> Local() => await t;\n"));

    [Fact]
    public Task AsyncAnonymousMethod() => FixAsync(
        Class("        Func<Task> f = async delegate { {|NLR0025:u.Wait()|}; };\n"),
        Class("        Func<Task> f = async delegate { await u; };\n"));

    [Fact]
    public Task ResultInAsyncVoid() => FixAsync(
        Members("    async void M(Task<int> t)\n    {\n        var x = {|NLR0025:t.Result|};\n    }\n"),
        Members("    async void M(Task<int> t)\n    {\n        var x = await t;\n    }\n"));

    [Fact]
    public Task AwaitedAfterTheBlockingCallIsStillReported() => FixAsync(
        Class("        var x = {|NLR0025:t.Result|};\n        await t;\n"),
        Class("        var x = await t;\n        await t;\n"));

    [Fact]
    public Task OtherTaskThanTheAwaitedOne() => FixAsync(
        Class("        await u;\n        var x = {|NLR0025:t.Result|};\n"),
        Class("        await u;\n        var x = await t;\n"));

    [Fact]
    public Task CommentsAreKept() => FixAsync(
        Class("        // load\n        var x = /* now */ {|NLR0025:t.Result|}; // done\n"),
        Class("        // load\n        var x = /* now */ await t; // done\n"));

    [Fact]
    public Task TwoDiagnosticsFixAll() => FixAsync(
        Class("        var a = {|NLR0025:t.Result|};\n        {|NLR0025:u.Wait()|};\n        var b = {|NLR0025:v.GetAwaiter().GetResult()|};\n"),
        Class("        var a = await t;\n        await u;\n        var b = await v;\n"));

    // --- Reported, without fix -----------------------------------------------------

    [Fact]
    public Task WaitAllHasNoFix() => NoFixAsync(
        Class("        {|NLR0025:Task.WaitAll(t, u)|};\n"));

    [Fact]
    public Task WaitAnyHasNoFix() => NoFixAsync(
        Class("        var i = {|NLR0025:Task.WaitAny(t, u)|};\n"));

    [Fact]
    public Task WaitWithTimeoutHasNoFix() => NoFixAsync(
        Class("        var done = {|NLR0025:t.Wait(10)|};\n"));

    [Fact]
    public Task WaitWithTimeoutStatementHasNoFix() => NoFixAsync(
        Class("        {|NLR0025:u.Wait(10)|};\n"));

    [Fact]
    public Task InsideLockHasNoFix() => NoFixAsync(
        Class("        lock (o)\n        {\n            var x = {|NLR0025:t.Result|};\n        }\n"));

    [Fact]
    public Task InsideExceptionFilterHasNoFix() => NoFixAsync(
        Class("        try { }\n        catch (Exception) when ({|NLR0025:t.Result|} > 0) { }\n"));

    [Fact]
    public Task InsideQueryHasNoFix() => NoFixAsync(
        Class("        var q = from i in new[] { 1 } select i + {|NLR0025:t.Result|};\n"));

    [Fact]
    public Task InsideUnsafeBlockHasNoFix()
    {
        var source = Class("        unsafe\n        {\n            var x = {|NLR0025:t.Result|};\n        }\n");
        var test = new Test(source, source);
        test.SolutionTransforms.Add((solution, projectId) => solution.WithProjectCompilationOptions(
            projectId,
            ((CSharpCompilationOptions)solution.GetProject(projectId)!.CompilationOptions!).WithAllowUnsafe(true)));
        // Awaiting in unsafe code is an error before C# 13, which is the reason for the missing fix.
        return test.RunAsync();
    }

    [Fact]
    public Task LockInsideAsyncLambdaIsNotTheLambdasLock() => FixAsync(
        Class("        lock (o)\n        {\n            Func<Task<int>> f = async () => {|NLR0025:t.Result|};\n        }\n"),
        Class("        lock (o)\n        {\n            Func<Task<int>> f = async () => await t;\n        }\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task NotReportedSyncMethod() => NoDiagnosticAsync(
        Members("    int M(Task<int> t)\n    {\n        t.Wait();\n        return t.Result + t.GetAwaiter().GetResult();\n    }\n"));

    [Fact]
    public Task NotReportedSyncLambdaInAsyncMethod() => NoDiagnosticAsync(
        Class("        Func<int> f = () => t.Result;\n        Action a = () => u.Wait();\n"));

    [Fact]
    public Task NotReportedSyncLocalFunctionInAsyncMethod() => NoDiagnosticAsync(
        Class("        int Local() => t.Result;\n"));

    [Fact]
    public Task NotReportedExpressionTree() => NoDiagnosticAsync(
        Class("        System.Linq.Expressions.Expression<Func<int>> e = () => t.Result;\n"));

    [Fact]
    public Task NotReportedConstructorAndAccessor() => NoDiagnosticAsync(
        Members("    public C(Task<int> t)\n    {\n        var x = t.Result;\n    }\n\n    public int P(Task<int> t) => t.Result;\n"));

    [Fact]
    public Task NotReportedAfterAwaitOfTheSameTask() => NoDiagnosticAsync(
        Class("        await t;\n        var x = t.Result;\n        {\n            var y = t.GetAwaiter().GetResult();\n        }\n        await u;\n        u.Wait();\n"));

    [Fact]
    public Task NotReportedAfterConfiguredAwait() => NoDiagnosticAsync(
        Class("        await t.ConfigureAwait(false);\n        var x = t.Result;\n"));

    [Fact]
    public Task NotReportedAfterWhenAll() => NoDiagnosticAsync(
        Members("    async Task M(Task<int> a, Task<int> b)\n    {\n        await Task.WhenAll(a, b);\n        var x = a.Result + b.Result;\n    }\n"));

    [Fact]
    public Task NotReportedAfterWhenAny() => NoDiagnosticAsync(
        Members("    async Task M(Task<int> a, Task<int> b)\n    {\n        await Task.WhenAny(a, b).ConfigureAwait(false);\n        var x = a.Result;\n    }\n"));

    [Fact]
    public Task NotReportedAfterAwaitOfLocal() => NoDiagnosticAsync(
        Members("    async Task<int> M()\n    {\n        var task = Task.Run(() => 1);\n        await task;\n        return task.Result;\n    }\n"));

    [Fact]
    public Task NotReportedFromResultAndCompletedTask() => NoDiagnosticAsync(
        Class("        var x = Task.FromResult(1).Result;\n        Task.CompletedTask.Wait();\n"));

    [Fact]
    public Task NotReportedResultOfAnotherType() => NoDiagnosticAsync(
        Class("        var x = box.Result;\n"));

    [Fact]
    public Task NotReportedAwait() => NoDiagnosticAsync(
        Class("        var x = await t;\n        await u;\n        await Task.WhenAll(t, u);\n"));
}
