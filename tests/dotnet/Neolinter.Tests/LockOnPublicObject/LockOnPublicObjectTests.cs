using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.LockOnPublicObject;

namespace Neolinter.Tests.LockOnPublicObject;

/// <summary>NLR0031 tests (report only, no code fix).</summary>
public class LockOnPublicObjectTests
{
    private static Task VerifyAsync(string source) => new CSharpAnalyzerTest<LockOnPublicObjectAnalyzer, DefaultVerifier>
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    }.RunAsync();

    private static string Class(string modifiers, string lockExpression) =>
        "using System;\n\n" + modifiers + " class C\n{\n    private readonly object _gate = new();\n    private const string Name = \"x\";\n    private int _n;\n\n    void M()\n    {\n        lock (" + lockExpression + ")\n        {\n            _n++;\n        }\n    }\n}\n";

    // --- Reported -------------------------------------------------------------------

    [Fact]
    public Task LockThis() => VerifyAsync(Class("public", "{|NLR0031:this|}"));

    [Fact]
    public Task LockThisInParentheses() => VerifyAsync(Class("public", "{|NLR0031:(this)|}"));

    [Fact]
    public Task LockThisInInternalUnsealedClass() => VerifyAsync(Class("internal", "{|NLR0031:this|}"));

    [Fact]
    public Task LockThisInAbstractClass() => VerifyAsync(Class("public abstract", "{|NLR0031:this|}"));

    [Fact]
    public Task LockStringLiteral() => VerifyAsync(Class("public", "{|NLR0031:\"gate\"|}"));

    [Fact]
    public Task LockStringConstant() => VerifyAsync(Class("public", "{|NLR0031:Name|}"));

    [Fact]
    public Task LockStringExpression() => VerifyAsync(Class("public", "{|NLR0031:Name + \"y\"|}"));

    [Fact]
    public Task LockTypeof() => VerifyAsync(Class("public", "{|NLR0031:typeof(C)|}"));

    [Fact]
    public Task LockGetType() => VerifyAsync(Class("public", "{|NLR0031:GetType()|}"));

    [Fact]
    public Task LockBoxedValue() => VerifyAsync(Class("public", "{|NLR0031:(object)_n|}"));

    [Fact]
    public Task LockThisInLambda() => VerifyAsync(
        "using System;\n\npublic class C\n{\n    void M()\n    {\n        Action a = () =>\n        {\n            lock ({|NLR0031:this|})\n            {\n            }\n        };\n        a();\n    }\n}\n");

    [Fact]
    public Task LockThisInPublicNestedTypeOfPublicType() => VerifyAsync(
        "public class Outer\n{\n    public class Inner\n    {\n        void M()\n        {\n            lock ({|NLR0031:this|})\n            {\n            }\n        }\n    }\n}\n");

    // --- Not reported ---------------------------------------------------------------

    [Fact]
    public Task NotReportedPrivateReadonlyObject() => VerifyAsync(Class("public", "_gate"));

    [Fact]
    public Task NotReportedLockThisInSealedClass() => VerifyAsync(Class("public sealed", "this"));

    [Fact]
    public Task NotReportedLockThisInPrivateNestedType() => VerifyAsync(
        "public class Outer\n{\n    private class Inner\n    {\n        void M()\n        {\n            lock (this)\n            {\n            }\n        }\n    }\n}\n");

    [Fact]
    public Task NotReportedLockThisInTypeNestedInPrivateType() => VerifyAsync(
        "public class Outer\n{\n    private class Mid\n    {\n        public class Inner\n        {\n            void M()\n            {\n                lock (this)\n                {\n                }\n            }\n        }\n    }\n}\n");

    [Fact]
    public Task NotReportedOtherReferenceTypes() => VerifyAsync(
        "using System.Collections.Generic;\n\nclass C\n{\n    private readonly List<int> _items = new();\n\n    void M(List<int> other)\n    {\n        lock (_items)\n        {\n        }\n        lock (other)\n        {\n        }\n    }\n}\n");

    [Fact]
    public Task NotReportedCastOfReferenceType() => VerifyAsync(
        "class C\n{\n    private readonly System.Collections.Generic.List<int> _l = new();\n\n    void M()\n    {\n        lock ((object)_l)\n        {\n        }\n    }\n}\n");
}
