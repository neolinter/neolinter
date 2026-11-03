using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.EqualsWithoutGetHashCode;

namespace Neolinter.Tests.EqualsWithoutGetHashCode;

/// <summary>NLR0033 tests (report only, no code fix).</summary>
public class EqualsWithoutGetHashCodeTests
{
    private static Task VerifyAsync(string source) => new CSharpAnalyzerTest<EqualsWithoutGetHashCodeAnalyzer, DefaultVerifier>
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    }.RunAsync();

    private const string EqualsObject = "    public override bool Equals(object? obj) => obj is C c && c.Id == Id;\n";
    private const string Hash = "    public override int GetHashCode() => Id;\n";

    private static string Body(string members, string header = "public class C") =>
        "using System;\n\n" + header + "\n{\n    public int Id;\n" + members + "}\n";

    // --- Reported -------------------------------------------------------------------

    [Fact]
    public Task EqualsObjectOnly() => VerifyAsync(Body(EqualsObject, "public class {|NLR0033:C|}"));

    [Fact]
    public Task EqualsObjectOnlyInStruct() => VerifyAsync(Body(EqualsObject, "public struct {|NLR0033:C|}"));

    [Fact]
    public Task EqualityOperatorsOnly() => VerifyAsync(Body(
        "    public static bool operator ==(C? a, C? b) => a?.Id == b?.Id;\n    public static bool operator !=(C? a, C? b) => !(a == b);\n",
        "public class {|NLR0033:C|}"));

    [Fact]
    public Task EquatableImplicitOnly() => VerifyAsync(Body(
        "    public bool Equals(C? other) => other?.Id == Id;\n", "public class {|NLR0033:C|} : IEquatable<C>"));

    [Fact]
    public Task EquatableExplicitOnly() => VerifyAsync(Body(
        "    bool IEquatable<C>.Equals(C? other) => other?.Id == Id;\n", "public class {|NLR0033:C|} : IEquatable<C>"));

    [Fact]
    public Task DerivedOverridesEqualsOnly() => VerifyAsync(
        "using System;\n\npublic class B\n{\n}\n\npublic class {|NLR0033:C|} : B\n{\n    public override bool Equals(object? obj) => true;\n}\n");

    [Fact]
    public Task GetHashCodeOnly() => VerifyAsync(Body("    public override int {|NLR0033:GetHashCode|}() => Id;\n"));

    // --- Not reported ---------------------------------------------------------------

    [Fact]
    public Task BothOverridden() => VerifyAsync(Body(EqualsObject + Hash));

    [Fact]
    public Task BothOverriddenInStruct() => VerifyAsync(Body(EqualsObject + Hash, "public struct C"));

    [Fact]
    public Task GetHashCodeWithEquatableOnly() => VerifyAsync(Body(
        "    public bool Equals(C? other) => other?.Id == Id;\n    public override int GetHashCode() => Id;\n", "public class C : IEquatable<C>"));

    [Fact]
    public Task Neither() => VerifyAsync(Body(""));

    [Fact]
    public Task Record() => VerifyAsync("public record R(int Id);\n");

    [Fact]
    public Task RecordStruct() => VerifyAsync("public record struct R(int Id);\n");

    [Fact]
    public Task RecordOverridingEquals() => VerifyAsync("public record R(int Id)\n{\n    public virtual bool Equals(R? other) => other?.Id == Id;\n}\n");

    [Fact]
    public Task AbstractClass() => VerifyAsync(Body(EqualsObject, "public abstract class C"));

    [Fact]
    public Task StaticClass() => VerifyAsync("public static class C\n{\n    public static bool Equals(string a, string b, int c) => true;\n}\n");

    [Fact]
    public Task StaticEqualsOverload() => VerifyAsync(Body("    public static bool Equals(C a, C b) => a.Id == b.Id;\n"));

    [Fact]
    public Task InstanceEqualsOverloadNotObject() => VerifyAsync(Body("    public bool Equals(C? other) => other?.Id == Id;\n"));

    [Fact]
    public Task EqualsOverloadWithTwoParameters() => VerifyAsync(Body("    public bool Equals(C other, int mode) => other.Id == Id;\n"));

    [Fact]
    public Task BaseClassOverridesGetHashCode() => VerifyAsync(
        "using System;\n\npublic class B\n{\n    public override int GetHashCode() => 1;\n    public override bool Equals(object? obj) => true;\n}\n\npublic class C : B\n{\n    public override bool Equals(object? obj) => false;\n}\n");

    [Fact]
    public Task BaseClassOverridesEquals() => VerifyAsync(
        "using System;\n\npublic class B\n{\n    public override bool Equals(object? obj) => true;\n    public override int GetHashCode() => 1;\n}\n\npublic class C : B\n{\n    public override int GetHashCode() => 2;\n}\n");

    [Fact]
    public Task EquatableImplementedByBaseOnly() => VerifyAsync(
        "using System;\n\npublic class B : IEquatable<B>\n{\n    public bool Equals(B? other) => true;\n    public override bool Equals(object? obj) => true;\n    public override int GetHashCode() => 1;\n}\n\npublic class C : B\n{\n}\n");

    [Fact]
    public Task Interface() => VerifyAsync("public interface I\n{\n    bool Equals(I other);\n}\n");
}
