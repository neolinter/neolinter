using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.MutableStruct;

namespace Neolinter.Tests.MutableStruct;

/// <summary>NLR0034 tests (report only, no code fix).</summary>
public class MutableStructTests
{
    private static Task VerifyAsync(string source) => new CSharpAnalyzerTest<MutableStructAnalyzer, DefaultVerifier>
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    }.RunAsync();

    // --- Reported -------------------------------------------------------------------

    [Fact]
    public Task MutableField() => VerifyAsync("public struct {|NLR0034:S|}\n{\n    public int X;\n}\n");

    [Fact]
    public Task PrivateField() => VerifyAsync("public struct {|NLR0034:S|}\n{\n    private int x;\n}\n");

    [Fact]
    public Task PropertyWithSetter() => VerifyAsync("public struct {|NLR0034:S|}\n{\n    public int X { get; set; }\n}\n");

    [Fact]
    public Task PrivateSetter() => VerifyAsync("public struct {|NLR0034:S|}\n{\n    public int X { get; private set; }\n}\n");

    [Fact]
    public Task ManualPropertyWithSetter() => VerifyAsync(
        "public struct {|NLR0034:S|}\n{\n    private readonly int x;\n    public int X { get => x; set { } }\n}\n");

    [Fact]
    public Task RefStruct() => VerifyAsync("public ref struct {|NLR0034:S|}\n{\n    public int X;\n}\n");

    [Fact]
    public Task InteropStruct() => VerifyAsync(
        "using System.Runtime.InteropServices;\n\n[StructLayout(LayoutKind.Sequential)]\npublic struct {|NLR0034:S|}\n{\n    public int X;\n}\n");

    [Fact]
    public Task PartialStructReportedOnce() => VerifyAsync(
        "public partial struct {|NLR0034:S|}\n{\n    public int X;\n}\n\npublic partial struct S\n{\n    public int Y;\n}\n");

    [Fact]
    public Task NestedStruct() => VerifyAsync(
        "public class C\n{\n    public struct {|NLR0034:S|}\n    {\n        public int X;\n    }\n}\n");

    // --- Not reported ---------------------------------------------------------------

    [Fact]
    public Task NotReportedReadonlyStruct() => VerifyAsync("public readonly struct S\n{\n    public readonly int X;\n    public int Y { get; }\n}\n");

    [Fact]
    public Task NotReportedRecordStruct() => VerifyAsync("public record struct S(int X)\n{\n    public int Y { get; set; }\n}\n");

    [Fact]
    public Task NotReportedReadonlyFields() => VerifyAsync("public struct S\n{\n    public readonly int X;\n    public int Y { get; }\n}\n");

    [Fact]
    public Task NotReportedInitOnly() => VerifyAsync("public struct S\n{\n    public int X { get; init; }\n}\n");

    [Fact]
    public Task NotReportedStaticAndConst() => VerifyAsync(
        "public struct S\n{\n    public static int Count;\n    public static int Total { get; set; }\n    public const int Max = 3;\n}\n");

    [Fact]
    public Task NotReportedStaticEvent() => VerifyAsync("using System;\n\npublic struct S\n{\n    public static event EventHandler? Changed;\n}\n");

    [Fact]
    public Task NotReportedClass() => VerifyAsync("public class C\n{\n    public int X;\n    public int Y { get; set; }\n}\n");

    [Fact]
    public Task NotReportedStructWithoutState() => VerifyAsync("public struct S\n{\n    public int Sum(int a, int b) => a + b;\n}\n");
}
