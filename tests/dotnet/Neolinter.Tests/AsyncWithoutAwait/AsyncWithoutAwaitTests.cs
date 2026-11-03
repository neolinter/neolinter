using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.AsyncWithoutAwait;
using Neolinter.CodeFixes.AsyncWithoutAwait;

namespace Neolinter.Tests.AsyncWithoutAwait;

/// <summary>NLR0027 tests. Several diagnostics in one source also exercise fix all.</summary>
public class AsyncWithoutAwaitTests
{
    private sealed class Test : CSharpCodeFixTest<AsyncWithoutAwaitAnalyzer, AsyncWithoutAwaitCodeFixProvider, DefaultVerifier>
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

    private const string Header = "using System;\nusing System.Collections.Generic;\nusing System.Threading.Tasks;\n\n";

    private static string Members(string members) => Header + "class C\n{\n" + members + "}\n";

    // The enclosing method awaits, so only the code under test is reported.
    private static string InMethod(string body) => Members("    async Task M()\n    {\n        await Task.Yield();\n" + body + "    }\n");

    // --- Reported, with fix --------------------------------------------------------

    [Fact]
    public Task TaskMethodGetsCompletedTask() => FixAsync(
        Members("    async Task {|NLR0027:M|}()\n    {\n        Console.WriteLine();\n    }\n"),
        Members("    Task M()\n    {\n        Console.WriteLine();\n        return Task.CompletedTask;\n    }\n"));

    [Fact]
    public Task GenericTaskMethodGetsFromResult() => FixAsync(
        Members("    async Task<int> {|NLR0027:M|}()\n    {\n        return 1;\n    }\n"),
        Members("    Task<int> M()\n    {\n        return Task.FromResult<int>(1);\n    }\n"));

    [Fact]
    public Task FromResultKeepsTheDeclaredTypeArgument() => FixAsync(
        Members("    async Task<object> {|NLR0027:M|}(bool b)\n    {\n        if (b)\n        {\n            return \"a\";\n        }\n\n        return null;\n    }\n"),
        Members("    Task<object> M(bool b)\n    {\n        if (b)\n        {\n            return Task.FromResult<object>(\"a\");\n        }\n\n        return Task.FromResult<object>(null);\n    }\n"));

    [Fact]
    public Task EarlyReturnAndFallThrough() => FixAsync(
        Members("    async Task {|NLR0027:M|}(bool b)\n    {\n        if (b)\n        {\n            return;\n        }\n\n        Console.WriteLine();\n    }\n"),
        Members("    Task M(bool b)\n    {\n        if (b)\n        {\n            return Task.CompletedTask;\n        }\n\n        Console.WriteLine();\n        return Task.CompletedTask;\n    }\n"));

    [Fact]
    public Task EndNotReachableAddsNothing() => FixAsync(
        Members("    async Task {|NLR0027:M|}()\n    {\n        while (true)\n        {\n            Console.WriteLine();\n        }\n    }\n"),
        Members("    Task M()\n    {\n        while (true)\n        {\n            Console.WriteLine();\n        }\n    }\n"));

    [Fact]
    public Task ExpressionBodyOfGenericTask() => FixAsync(
        Members("    async Task<int> {|NLR0027:M|}() => 5;\n"),
        Members("    Task<int> M() => Task.FromResult<int>(5);\n"));

    [Fact]
    public Task ModifiersAndCommentsArePreserved() => FixAsync(
        Members("    // doc\n    [Obsolete]\n    public static async Task<string> {|NLR0027:M|}()\n    {\n        // value\n        return \"x\"; // done\n    }\n"),
        Members("    // doc\n    [Obsolete]\n    public static Task<string> M()\n    {\n        // value\n        return Task.FromResult<string>(\"x\"); // done\n    }\n"));

    [Fact]
    public Task AsyncIsTheFirstModifierAndKeepsTheIndentation() => FixAsync(
        Members("    // doc\n    async Task {|NLR0027:M|}() { Console.WriteLine(); }\n"),
        Members("    // doc\n    Task M() { Console.WriteLine(); return Task.CompletedTask; }\n"));

    [Fact]
    public Task QualifiedReturnType() => FixAsync(
        "class C\n{\n    async System.Threading.Tasks.Task<int> {|NLR0027:M|}()\n    {\n        return 1;\n    }\n}\n",
        "class C\n{\n    System.Threading.Tasks.Task<int> M()\n    {\n        return System.Threading.Tasks.Task.FromResult<int>(1);\n    }\n}\n");

    [Fact]
    public Task AnotherTaskTypeInScopeIsQualified() => FixAsync(
        "class Task { }\n\nclass C\n{\n    async System.Threading.Tasks.Task {|NLR0027:M|}()\n    {\n        System.Console.WriteLine();\n    }\n}\n",
        "class Task { }\n\nclass C\n{\n    System.Threading.Tasks.Task M()\n    {\n        System.Console.WriteLine();\n        return System.Threading.Tasks.Task.CompletedTask;\n    }\n}\n");

    [Fact]
    public Task LocalFunction() => FixAsync(
        InMethod("        async Task<int> {|NLR0027:Local|}() { return 1; }\n        await Local();\n"),
        InMethod("        Task<int> Local() { return Task.FromResult<int>(1); }\n        await Local();\n"));

    [Fact]
    public Task ReturnsOfNestedLambdasAreLeftAlone() => FixAsync(
        Members("    async Task<int> {|NLR0027:M|}()\n    {\n        Func<int> f = () => { return 2; };\n        return f();\n    }\n"),
        Members("    Task<int> M()\n    {\n        Func<int> f = () => { return 2; };\n        return Task.FromResult<int>(f());\n    }\n"));

    [Fact]
    public Task FixAllOnSeveralMethods() => FixAsync(
        Members("    async Task {|NLR0027:A|}()\n    {\n        Console.WriteLine();\n    }\n\n    async Task<int> {|NLR0027:B|}() => 1;\n"),
        Members("    Task A()\n    {\n        Console.WriteLine();\n        return Task.CompletedTask;\n    }\n\n    Task<int> B() => Task.FromResult<int>(1);\n"));

    [Fact]
    public Task OverrideAndInterfaceImplementationAreFixedToo() => FixAsync(
        Members("    public async Task {|NLR0027:Run|}()\n    {\n        Console.WriteLine();\n    }\n"),
        Members("    public Task Run()\n    {\n        Console.WriteLine();\n        return Task.CompletedTask;\n    }\n"));

    // --- Reported, no fix ----------------------------------------------------------

    [Fact]
    public Task LambdaIsReportedWithoutFix() => UnchangedAsync(
        InMethod("        Func<Task<int>> f = {|NLR0027:async|} () => 1;\n        await f();\n"));

    [Fact]
    public Task AnonymousMethodIsReportedWithoutFix() => UnchangedAsync(
        InMethod("        Func<Task> f = {|NLR0027:async|} delegate { Console.WriteLine(); };\n        await f();\n"));

    [Fact]
    public Task ValueTaskIsReportedWithoutFix() => UnchangedAsync(
        Members("    async ValueTask<int> {|NLR0027:M|}() => 1;\n"));

    [Fact]
    public Task ThrowInTheBodyIsReportedWithoutFix() => UnchangedAsync(
        Members("    async Task {|NLR0027:M|}(string s)\n    {\n        if (s is null)\n        {\n            throw new ArgumentNullException(nameof(s));\n        }\n\n        Console.WriteLine(s);\n    }\n"));

    [Fact]
    public Task UsingAndTryAreReportedWithoutFix() => UnchangedAsync(
        Members("    async Task {|NLR0027:A|}()\n    {\n        using var d = new System.IO.StringReader(\"\");\n        Console.WriteLine();\n    }\n\n    async Task {|NLR0027:B|}()\n    {\n        try { Console.WriteLine(); } catch { }\n    }\n"));

    [Fact]
    public Task ExpressionBodyOfPlainTaskIsReportedWithoutFix() => UnchangedAsync(
        Members("    async Task {|NLR0027:M|}() => Console.WriteLine();\n"));

    [Fact]
    public Task AwaitInsideANestedLambdaDoesNotCount() => FixAsync(
        Members("    async Task {|NLR0027:M|}()\n    {\n        Func<Task> f = async () => await Task.Yield();\n        _ = f;\n        Console.WriteLine();\n    }\n"),
        Members("    Task M()\n    {\n        Func<Task> f = async () => await Task.Yield();\n        _ = f;\n        Console.WriteLine();\n        return Task.CompletedTask;\n    }\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task NotReportedAwait() => UnchangedAsync(
        Members("    async Task M()\n    {\n        await Task.Delay(1);\n    }\n"));

    [Fact]
    public Task NotReportedAwaitForeach() => UnchangedAsync(
        Members("    async Task M(IAsyncEnumerable<int> items)\n    {\n        await foreach (var i in items)\n        {\n            Console.WriteLine(i);\n        }\n    }\n"));

    [Fact]
    public Task NotReportedAwaitUsing() => UnchangedAsync(
        Members("    async Task A(IAsyncDisposable d)\n    {\n        await using (d)\n        {\n            Console.WriteLine();\n        }\n    }\n\n    async Task B(IAsyncDisposable d)\n    {\n        await using var x = d;\n        Console.WriteLine();\n    }\n"));

    [Fact]
    public Task NotReportedAsyncVoid() => UnchangedAsync(
        Members("    async void M()\n    {\n        Console.WriteLine();\n    }\n"));

    [Fact]
    public Task NotReportedAsyncIterator() => UnchangedAsync(
        Members("    async IAsyncEnumerable<int> M()\n    {\n        yield return 1;\n    }\n"));

    [Fact]
    public Task NotReportedEmptyBodyAndStubs() => UnchangedAsync(
        Members("    public virtual async Task A()\n    {\n    }\n\n    async Task B() { }\n\n    async Task<int> D() => throw new NotImplementedException();\n"));

    [Fact]
    public Task NotReportedThrowOnly() => UnchangedAsync(
        Members("    async Task M()\n    {\n        throw new NotSupportedException();\n    }\n"));

    [Fact]
    public Task NotReportedSyncMethodReturningTask() => UnchangedAsync(
        Members("    Task M()\n    {\n        return Task.CompletedTask;\n    }\n"));

    [Fact]
    public Task NotReportedAsyncVoidLambda() => UnchangedAsync(
        InMethod("        Action a = async () => Console.WriteLine();\n        a();\n"));
}
