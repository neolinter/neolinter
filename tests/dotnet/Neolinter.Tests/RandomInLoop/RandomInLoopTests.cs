using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.RandomInLoop;

namespace Neolinter.Tests.RandomInLoop;

/// <summary>NLR0036 tests (report only, no code fix).</summary>
public class RandomInLoopTests
{
    private static Task VerifyAsync(string source) => new CSharpAnalyzerTest<RandomInLoopAnalyzer, DefaultVerifier>
    {
        TestCode = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing System.Security.Cryptography;\n\n"
            + "public class C\n{\n" + source + "}\n",
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    }.RunAsync();

    private static Task InMethod(string body) => VerifyAsync("    void M(List<int> xs)\n    {\n" + body + "    }\n");

    // --- Reported -------------------------------------------------------------------

    [Fact]
    public Task ForBody() => InMethod("        for (var i = 0; i < 3; i++)\n        {\n            var r = {|NLR0036:new Random()|};\n            r.Next();\n        }\n");

    [Fact]
    public Task ForeachBody() => InMethod("        foreach (var x in xs)\n            Console.WriteLine({|NLR0036:new Random()|}.Next(x));\n");

    [Fact]
    public Task WhileBody() => InMethod("        while (xs.Count < 3)\n        {\n            xs.Add({|NLR0036:new Random()|}.Next());\n        }\n");

    [Fact]
    public Task DoWhileBody() => InMethod("        do\n        {\n            xs.Add({|NLR0036:new Random()|}.Next());\n        } while (xs.Count < 3);\n");

    [Fact]
    public Task TargetTyped() => InMethod("        foreach (var x in xs)\n        {\n            Random r = {|NLR0036:new()|};\n            r.Next(x);\n        }\n");

    [Fact]
    public Task WhileCondition() => InMethod("        while ({|NLR0036:new Random()|}.Next() > 5)\n        {\n        }\n");

    [Fact]
    public Task NestedInIf() => InMethod("        foreach (var x in xs)\n        {\n            if (x > 0)\n            {\n                var r = {|NLR0036:new Random()|};\n            }\n        }\n");

    [Fact]
    public Task LoopInsideLambda() => InMethod("        Action a = () =>\n        {\n            foreach (var x in xs)\n                {|NLR0036:new Random()|}.Next();\n        };\n");

    // --- Not reported ---------------------------------------------------------------

    [Fact]
    public Task NotReportedExplicitSeed() => InMethod("        foreach (var x in xs)\n            new Random(x).Next();\n");

    [Fact]
    public Task NotReportedOutsideLoop() => InMethod("        var r = new Random();\n        foreach (var x in xs)\n            r.Next(x);\n");

    [Fact]
    public Task NotReportedSharedInstance() => InMethod("        foreach (var x in xs)\n            Random.Shared.Next(x);\n");

    [Fact]
    public Task NotReportedCryptoRandom() => InMethod("        foreach (var x in xs)\n            RandomNumberGenerator.GetInt32(x + 1);\n");

    [Fact]
    public Task NotReportedLambdaInLoop() => InMethod("        foreach (var x in xs)\n        {\n            Func<int> f = () => new Random().Next();\n        }\n");

    [Fact]
    public Task NotReportedLocalFunctionInLoop() => InMethod("        foreach (var x in xs)\n        {\n            int F() => new Random().Next();\n        }\n");

    [Fact]
    public Task NotReportedForeachCollection() => InMethod("        foreach (var x in Enumerable.Range(0, new Random().Next(5)))\n        {\n        }\n");

    [Fact]
    public Task NotReportedForInitializer() => InMethod("        for (var r = new Random(); xs.Count < 3; xs.Add(r.Next()))\n        {\n        }\n");

    [Fact]
    public Task NotReportedFieldInitializer() => VerifyAsync("    private readonly Random field = new Random();\n");

    [Fact]
    public Task NotReportedOtherRandomType() => VerifyAsync("    class Random { }\n    void M(List<int> xs)\n    {\n        foreach (var x in xs)\n            new Random();\n    }\n");
}
