using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.AsyncVoid;

namespace Neolinter.Tests.AsyncVoid;

/// <summary>NLR0024 tests. The rule has no code fix.</summary>
public class AsyncVoidTests
{
    private sealed class Test : CSharpAnalyzerTest<AsyncVoidAnalyzer, DefaultVerifier>
    {
        public Test(string source)
        {
            TestCode = source;
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        }
    }

    private static Task RunAsync(string source) => new Test(source).RunAsync();

    private static string Class(string body) =>
        "using System;\nusing System.Threading.Tasks;\n\ninterface I\n{\n    void Run();\n}\n\nclass Base\n{\n    public virtual void Virtual() { }\n}\n\nclass C : Base\n{\n    public event EventHandler Changed;\n    public event Action Ticked;\n\n    static Task Work() => Task.CompletedTask;\n\n" + body + "}\n";

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task Method() => RunAsync(Class(
        "    async void {|NLR0024:M|}()\n    {\n        await Work();\n    }\n"));

    [Fact]
    public Task StaticMethodWithParameters() => RunAsync(Class(
        "    static async void {|NLR0024:M|}(int a, string b)\n    {\n        await Work();\n    }\n"));

    [Fact]
    public Task ObjectAndNonEventArgs() => RunAsync(Class(
        "    async void {|NLR0024:M|}(object sender, string e)\n    {\n        await Work();\n    }\n"));

    [Fact]
    public Task MethodCalledNormally() => RunAsync(Class(
        "    async void {|NLR0024:M|}()\n    {\n        await Work();\n    }\n\n    void Caller() => M();\n"));

    [Fact]
    public Task LocalFunction() => RunAsync(Class(
        "    void M()\n    {\n        async void {|NLR0024:Local|}() => await Work();\n        Local();\n    }\n"));

    [Fact]
    public Task LambdaToAction() => RunAsync(Class(
        "    void M()\n    {\n        Action a = {|NLR0024:async|} () => await Work();\n        a();\n    }\n"));

    [Fact]
    public Task LambdaToActionOfT() => RunAsync(Class(
        "    void M()\n    {\n        Action<int> a = {|NLR0024:async|} x => await Work();\n        a(1);\n    }\n"));

    [Fact]
    public Task AnonymousMethod() => RunAsync(Class(
        "    void M()\n    {\n        Action a = {|NLR0024:async|} delegate { await Work(); };\n        a();\n    }\n"));

    [Fact]
    public Task LambdaPassedAsArgument() => RunAsync(Class(
        "    static void Take(Action a) { }\n\n    void M()\n    {\n        Take({|NLR0024:async|} () => await Work());\n    }\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task NotReportedEventHandlerSignature() => RunAsync(Class(
        "    async void OnChanged(object sender, EventArgs e)\n    {\n        await Work();\n    }\n"));

    [Fact]
    public Task NotReportedDerivedEventArgs() => RunAsync(Class(
        "    async void OnChanged(object sender, ConsoleCancelEventArgs e)\n    {\n        await Work();\n    }\n"));

    [Fact]
    public Task NotReportedOverride() => RunAsync(Class(
        "    public override async void Virtual()\n    {\n        await Work();\n    }\n"));

    [Fact]
    public Task NotReportedInterfaceImplementation() => RunAsync(
        "using System.Threading.Tasks;\n\ninterface I\n{\n    void Run();\n}\n\nclass C : I\n{\n    public async void Run()\n    {\n        await Task.Delay(1);\n    }\n}\n");

    [Fact]
    public Task NotReportedExplicitInterfaceImplementation() => RunAsync(
        "using System.Threading.Tasks;\n\ninterface I\n{\n    void Run();\n}\n\nclass C : I\n{\n    async void I.Run()\n    {\n        await Task.Delay(1);\n    }\n}\n");

    [Fact]
    public Task NotReportedSubscribedMethodGroup() => RunAsync(Class(
        "    async void OnTick()\n    {\n        await Work();\n    }\n\n    void Subscribe() => Ticked += OnTick;\n"));

    [Fact]
    public Task NotReportedMethodGroupToAction() => RunAsync(Class(
        "    async void Go()\n    {\n        await Work();\n    }\n\n    void M()\n    {\n        Action a = Go;\n        a();\n    }\n"));

    [Fact]
    public Task NotReportedMethodGroupInAnotherClass() => RunAsync(
        "using System;\nusing System.Threading.Tasks;\n\nclass C\n{\n    public async void Go()\n    {\n        await Task.Delay(1);\n    }\n}\n\nclass D\n{\n    void M(C c)\n    {\n        Action a = c.Go;\n        a();\n    }\n}\n");

    [Fact]
    public Task NotReportedLocalFunctionAsDelegate() => RunAsync(Class(
        "    void M()\n    {\n        async void Local() => await Work();\n        Action a = Local;\n        a();\n    }\n"));

    [Fact]
    public Task NotReportedTaskReturning() => RunAsync(Class(
        "    async Task M()\n    {\n        await Work();\n    }\n"));

    [Fact]
    public Task NotReportedSyncVoid() => RunAsync(Class(
        "    void M()\n    {\n    }\n"));

    [Fact]
    public Task NotReportedLambdaToFuncTask() => RunAsync(Class(
        "    void M()\n    {\n        Func<Task> f = async () => await Work();\n        f();\n    }\n"));

    [Fact]
    public Task NotReportedLambdaAsTaskRunArgument() => RunAsync(Class(
        "    void M()\n    {\n        Task.Run(async () => await Work());\n    }\n"));

    [Fact]
    public Task NotReportedSyncLambdaToAction() => RunAsync(Class(
        "    void M()\n    {\n        Action a = () => { };\n        a();\n    }\n"));

    [Fact]
    public Task NotReportedLambdaAddedToEvent() => RunAsync(Class(
        "    void M()\n    {\n        Changed += async (s, e) => await Work();\n        Ticked += async () => await Work();\n    }\n"));

    [Fact]
    public Task NotReportedLambdaWithEventHandlerSignature() => RunAsync(Class(
        "    void M()\n    {\n        EventHandler h = async (s, e) => await Work();\n        h(null, EventArgs.Empty);\n    }\n"));
}
