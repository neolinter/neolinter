using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.ThreadSleep;
using Neolinter.CodeFixes.ThreadSleep;

namespace Neolinter.Tests.ThreadSleep;

/// <summary>NLR0026 tests. Several diagnostics in one source also exercise fix all.</summary>
public class ThreadSleepTests
{
    private sealed class Test : CSharpCodeFixTest<ThreadSleepAnalyzer, ThreadSleepCodeFixProvider, DefaultVerifier>
    {
        public Test(string source, string fixedSource)
        {
            TestCode = source;
            FixedCode = fixedSource;
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        }
    }

    private static Task FixAsync(string source, string fixedSource) => new Test(source, fixedSource).RunAsync();

    /// <summary>No diagnostic, or reported but not fixable: the source must stay as it is.</summary>
    private static Task UnchangedAsync(string source) => new Test(source, source).RunAsync();

    private const string Header = "using System;\nusing System.Threading;\nusing System.Threading.Tasks;\n\n";

    private static string Body(string body) => Header + "class C\n{\n    async Task M(TimeSpan wait)\n    {\n" + body + "    }\n}\n";

    private static string Members(string members) => Header + "class C\n{\n" + members + "}\n";

    // --- Reported, with fix --------------------------------------------------------

    [Fact]
    public Task Milliseconds() => FixAsync(
        Body("        {|NLR0026:Thread.Sleep(500)|};\n"),
        Body("        await Task.Delay(500);\n"));

    [Fact]
    public Task TimeSpanArgument() => FixAsync(
        Body("        {|NLR0026:Thread.Sleep(wait)|};\n        {|NLR0026:Thread.Sleep(TimeSpan.FromSeconds(1))|};\n"),
        Body("        await Task.Delay(wait);\n        await Task.Delay(TimeSpan.FromSeconds(1));\n"));

    [Fact]
    public Task CommentsAndIndentationArePreserved() => FixAsync(
        Body("        // pause\n        {|NLR0026:Thread.Sleep(10)|}; // done\n"),
        Body("        // pause\n        await Task.Delay(10); // done\n"));

    [Fact]
    public Task QualifiedAndStaticUsing() => FixAsync(
        "using static System.Threading.Thread;\n\nclass C\n{\n    async System.Threading.Tasks.Task M()\n    {\n        {|NLR0026:System.Threading.Thread.Sleep(5)|};\n        {|NLR0026:Sleep(6)|};\n    }\n}\n",
        "using static System.Threading.Thread;\n\nclass C\n{\n    async System.Threading.Tasks.Task M()\n    {\n        await System.Threading.Tasks.Task.Delay(5);\n        await System.Threading.Tasks.Task.Delay(6);\n    }\n}\n");

    [Fact]
    public Task NamedArgument() => FixAsync(
        Body("        {|NLR0026:Thread.Sleep(millisecondsTimeout: 5)|};\n"),
        Body("        await Task.Delay(5);\n"));

    [Fact]
    public Task AsyncLocalFunctionAndAnonymousMethod() => FixAsync(
        Body("        async Task Local() { {|NLR0026:Thread.Sleep(2)|}; }\n        Func<Task> f = async delegate { {|NLR0026:Thread.Sleep(3)|}; };\n        await Local();\n        await f();\n"),
        Body("        async Task Local() { await Task.Delay(2); }\n        Func<Task> f = async delegate { await Task.Delay(3); };\n        await Local();\n        await f();\n"));

    [Fact]
    public Task AsyncLambdaWithBlock() => FixAsync(
        Body("        Func<Task> f = async () => { {|NLR0026:Thread.Sleep(1)|}; };\n        await f();\n"),
        Body("        Func<Task> f = async () => { await Task.Delay(1); };\n        await f();\n"));

    [Fact]
    public Task InsideCatchAndFinally() => FixAsync(
        Body("        try { }\n        catch { {|NLR0026:Thread.Sleep(1)|}; }\n        finally { {|NLR0026:Thread.Sleep(2)|}; }\n"),
        Body("        try { }\n        catch { await Task.Delay(1); }\n        finally { await Task.Delay(2); }\n"));

    [Fact]
    public Task AnotherTaskTypeInScopeIsQualified() => FixAsync(
        "using System.Threading;\n\nclass Task { }\n\nclass C\n{\n    async System.Threading.Tasks.Task M()\n    {\n        {|NLR0026:Thread.Sleep(1)|};\n    }\n}\n",
        "using System.Threading;\n\nclass Task { }\n\nclass C\n{\n    async System.Threading.Tasks.Task M()\n    {\n        await System.Threading.Tasks.Task.Delay(1);\n    }\n}\n");

    // --- Reported, no fix ----------------------------------------------------------

    [Fact]
    public Task InsideLockIsReportedWithoutFix() => UnchangedAsync(
        Members("    readonly object gate = new();\n\n    async Task M()\n    {\n        lock (gate)\n        {\n            {|NLR0026:Thread.Sleep(1)|};\n        }\n        await Task.Yield();\n    }\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task NotReportedSyncMethod() => UnchangedAsync(
        Members("    void M()\n    {\n        Thread.Sleep(1);\n    }\n"));

    [Fact]
    public Task NotReportedSyncLambdaAndLocalFunctionInAsyncMethod() => UnchangedAsync(
        Body("        Action a = () => Thread.Sleep(1);\n        void Local() => Thread.Sleep(2);\n        await Task.Run(a);\n        Local();\n"));

    [Fact]
    public Task NotReportedConstructorAndAccessor() => UnchangedAsync(
        Members("    public C()\n    {\n        Thread.Sleep(1);\n    }\n\n    public int P { get { Thread.Sleep(1); return 0; } }\n"));

    [Fact]
    public Task NotReportedZeroDelay() => UnchangedAsync(
        Body("        Thread.Sleep(0);\n        Thread.Sleep(TimeSpan.Zero);\n        await Task.Yield();\n"));

    [Fact]
    public Task NotReportedOtherSleepAndAwaitedDelay() => UnchangedAsync(
        Members("    static void Sleep(int ms) { }\n\n    async Task M()\n    {\n        Sleep(1);\n        await Task.Delay(1);\n    }\n"));
}
