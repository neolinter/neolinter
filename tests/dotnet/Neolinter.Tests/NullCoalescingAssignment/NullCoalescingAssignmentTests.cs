using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.NullCoalescingAssignment;
using Neolinter.CodeFixes.NullCoalescingAssignment;

namespace Neolinter.Tests.NullCoalescingAssignment;

/// <summary>NLR0004 tests. Several diagnostics in one source also exercise fix all.</summary>
public class NullCoalescingAssignmentTests
{
    private static Task FixAsync(string source, string fixedSource, LanguageVersion languageVersion = LanguageVersion.Latest) =>
        Run(source, fixedSource, languageVersion);

    private static Task NoDiagnosticAsync(string source, LanguageVersion languageVersion = LanguageVersion.Latest) =>
        Run(source, source, languageVersion);

    private static Task Run(string source, string fixedSource, LanguageVersion languageVersion)
    {
        var test = new CSharpCodeFixTest<NullCoalescingAssignmentAnalyzer, NullCoalescingAssignmentCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var options = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
            return solution.WithProjectParseOptions(projectId, options.WithLanguageVersion(languageVersion));
        });

        return test.RunAsync();
    }

    // --- Reported ------------------------------------------------------------

    [Theory]
    [InlineData("name == null")]
    [InlineData("null == name")]
    [InlineData("name is null")]
    public Task IfNull_Local(string condition) => FixAsync(
        $$"""
        class C
        {
            string M(string name)
            {
                {|NLR0004:if|} ({{condition}}) name = "x";
                return name;
            }
        }
        """,
        """
        class C
        {
            string M(string name)
            {
                name ??= "x";
                return name;
            }
        }
        """);

    [Fact]
    public Task IfNull_BracesAndLayoutAreKept() => FixAsync(
        """
        class C
        {
            string M(string name)
            {
                // keep me
                {|NLR0004:if|} (name == null)
                {
                    name = "x";
                } // trailing

                return name;
            }
        }
        """,
        """
        class C
        {
            string M(string name)
            {
                // keep me
                name ??= "x"; // trailing

                return name;
            }
        }
        """);

    [Fact]
    public Task Coalesce_Local() => FixAsync(
        """
        class C
        {
            string M(string name)
            {
                name {|NLR0004:=|} name ?? "x";
                return name;
            }
        }
        """,
        """
        class C
        {
            string M(string name)
            {
                name ??= "x";
                return name;
            }
        }
        """);

    [Fact]
    public Task FieldsPropertiesAndMemberPaths_FixAll() => FixAsync(
        """
        using System.Collections.Generic;

        class C
        {
            private List<int>? items;
            private static string? cache;
            public string? Name { get; set; }
            public C? Child;

            void M(C other)
            {
                {|NLR0004:if|} (items == null) items = new List<int>();
                {|NLR0004:if|} (this.items is null) { this.items = new List<int>(); }
                {|NLR0004:if|} (cache == null) cache = "c";
                {|NLR0004:if|} (C.cache == null) C.cache = "c";
                {|NLR0004:if|} (Name == null) Name = "n";
                {|NLR0004:if|} (other.Child == null) other.Child = this;
                this.Child {|NLR0004:=|} this.Child ?? this;
                Name {|NLR0004:=|} Name ?? "n";
            }
        }
        """,
        """
        using System.Collections.Generic;

        class C
        {
            private List<int>? items;
            private static string? cache;
            public string? Name { get; set; }
            public C? Child;

            void M(C other)
            {
                items ??= new List<int>();
                this.items ??= new List<int>();
                cache ??= "c";
                C.cache ??= "c";
                Name ??= "n";
                other.Child ??= this;
                this.Child ??= this;
                Name ??= "n";
            }
        }
        """);

    [Fact]
    public Task NullableValueType() => FixAsync(
        """
        class C
        {
            int? M(int? a, int? b)
            {
                {|NLR0004:if|} (a == null) a = 3;
                b {|NLR0004:=|} b ?? a;
                return a + b;
            }
        }
        """,
        """
        class C
        {
            int? M(int? a, int? b)
            {
                a ??= 3;
                b ??= a;
                return a + b;
            }
        }
        """);

    [Fact]
    public Task ValueWithAwait() => FixAsync(
        """
        using System.Threading.Tasks;

        class C
        {
            async Task<string> M(string? s)
            {
                {|NLR0004:if|} (s == null) s = await Task.FromResult("x");
                return s;
            }
        }
        """,
        """
        using System.Threading.Tasks;

        class C
        {
            async Task<string> M(string? s)
            {
                s ??= await Task.FromResult("x");
                return s;
            }
        }
        """);

    [Fact]
    public Task EmbeddedStatement() => FixAsync(
        """
        class C
        {
            void M(string[] items, string? s)
            {
                foreach (var item in items)
                    {|NLR0004:if|} (s == null) s = item;
            }
        }
        """,
        """
        class C
        {
            void M(string[] items, string? s)
            {
                foreach (var item in items)
                    s ??= item;
            }
        }
        """);

    [Fact]
    public Task AutoPropertyWithPrivateSetter() => FixAsync(
        """
        class C
        {
            public string? Name { get; private set; }

            void M() { Name {|NLR0004:=|} Name ?? "x"; }
        }
        """,
        """
        class C
        {
            public string? Name { get; private set; }

            void M() { Name ??= "x"; }
        }
        """);

    [Fact]
    public Task ClassConstrainedTypeParameter() => FixAsync(
        """
        class C
        {
            T M<T>(T value, T other) where T : class
            {
                {|NLR0004:if|} (value == null) value = other;
                return value;
            }
        }
        """,
        """
        class C
        {
            T M<T>(T value, T other) where T : class
            {
                value ??= other;
                return value;
            }
        }
        """);

    // --- Not reported --------------------------------------------------------

    [Fact]
    public Task OlderLanguageVersion_NoDiagnostic() => NoDiagnosticAsync(
        """
        class C
        {
            string M(string name)
            {
                if (name == null) name = "x";
                name = name ?? "y";
                return name;
            }
        }
        """,
        LanguageVersion.CSharp7_3);

    [Fact]
    public Task CSharp8_Reported() => FixAsync(
        """
        class C
        {
            string M(string name)
            {
                {|NLR0004:if|} (name == null) name = "x";
                return name;
            }
        }
        """,
        """
        class C
        {
            string M(string name)
            {
                name ??= "x";
                return name;
            }
        }
        """,
        LanguageVersion.CSharp8);

    [Fact]
    public Task DifferentTargets_NoDiagnostic() => NoDiagnosticAsync(
        """
        class C
        {
            void M(string a, string b)
            {
                if (a == null) b = "x";
                a = b ?? "x";
            }
        }
        """);

    [Fact]
    public Task ElseOrOtherStatements_NoDiagnostic() => NoDiagnosticAsync(
        """
        class C
        {
            void M(string a, string b)
            {
                if (a == null) a = "x"; else b = "y";
                if (a == null) { a = "x"; b = "y"; }
                if (a != null) a = "x";
                if (a == null && b == null) a = "x";
            }
        }
        """);

    [Fact]
    public Task SideEffectsInTarget_NoDiagnostic() => NoDiagnosticAsync(
        """
        class C
        {
            string[] values = new string[4];
            string? this[int i] { get => null; set { } }
            C? Other() => this;
            string? P { get; set; }

            void M(int i, string[] a)
            {
                if (a[i++] == null) a[i++] = "x";
                if (a[i] == null) a[i] = "x";
                a[i] = a[i] ?? "x";
                if (Other()!.P == null) Other()!.P = "x";
                if (this[0] == null) this[0] = "x";
                if (Other()!.Other()!.P == null) Other()!.Other()!.P = "x";
            }
        }
        """);

    [Fact]
    public Task PropertyInTheMiddleOfThePath_NoDiagnostic() => NoDiagnosticAsync(
        """
        class C
        {
            C Parent => this;
            string? Name { get; set; }

            void M()
            {
                if (Parent.Name == null) Parent.Name = "x";
            }
        }
        """);

    [Fact]
    public Task PropertyWithBodyOrVirtual_CoalesceFormNotReported_IfFormReported() => FixAsync(
        """
        class C
        {
            private string? name;
            public string? WithBody { get => name; set => name = value; }
            public virtual string? Overridable { get; set; }
            public string? Expression => name;

            void M()
            {
                WithBody = WithBody ?? "x";
                Overridable = Overridable ?? "x";
                {|NLR0004:if|} (WithBody == null) WithBody = "x";
                {|NLR0004:if|} (Overridable == null) Overridable = "x";
            }
        }
        """,
        """
        class C
        {
            private string? name;
            public string? WithBody { get => name; set => name = value; }
            public virtual string? Overridable { get; set; }
            public string? Expression => name;

            void M()
            {
                WithBody = WithBody ?? "x";
                Overridable = Overridable ?? "x";
                WithBody ??= "x";
                Overridable ??= "x";
            }
        }
        """);

    [Fact]
    public Task VolatileField_NoDiagnostic() => NoDiagnosticAsync(
        """
        class C
        {
            private volatile string? shared;

            void M()
            {
                if (shared == null) shared = "x";
            }
        }
        """);

    [Fact]
    public Task UserDefinedEquality_NoDiagnostic() => NoDiagnosticAsync(
        """
        class Money
        {
            public static bool operator ==(Money? a, Money? b) => ReferenceEquals(a, b);
            public static bool operator !=(Money? a, Money? b) => !(a == b);
            public override bool Equals(object? o) => base.Equals(o);
            public override int GetHashCode() => 0;
        }

        class C
        {
            void M(Money? m, Money other)
            {
                if (m == null) m = other;
            }
        }
        """);

    [Fact]
    public Task UserDefinedEquality_IsNullPatternIsStillReported() => FixAsync(
        """
        class Money
        {
            public static bool operator ==(Money? a, Money? b) => ReferenceEquals(a, b);
            public static bool operator !=(Money? a, Money? b) => !(a == b);
            public override bool Equals(object? o) => base.Equals(o);
            public override int GetHashCode() => 0;
        }

        class C
        {
            void M(Money? m, Money other)
            {
                {|NLR0004:if|} (m is null) m = other;
            }
        }
        """,
        """
        class Money
        {
            public static bool operator ==(Money? a, Money? b) => ReferenceEquals(a, b);
            public static bool operator !=(Money? a, Money? b) => !(a == b);
            public override bool Equals(object? o) => base.Equals(o);
            public override int GetHashCode() => 0;
        }

        class C
        {
            void M(Money? m, Money other)
            {
                m ??= other;
            }
        }
        """);

    [Fact]
    public Task CommentsInside_NoDiagnostic() => NoDiagnosticAsync(
        """
        class C
        {
            void M(string? a)
            {
                if (a == null)
                {
                    // default value
                    a = "x";
                }

                if (a == null) /* why */ a = "x";
                a = a /* why */ ?? "x";
            }
        }
        """);

    [Fact]
    public Task NullDefaultOrThrowValue_NoDiagnostic() => NoDiagnosticAsync(
        """
        using System;

        class C
        {
            void M(string? a)
            {
                if (a == null) a = null;
                if (a == null) a = default;
                a = a ?? throw new ArgumentNullException(nameof(a));
            }
        }
        """);

    [Fact]
    public Task DynamicAndUnconstrainedTypeParameter_NoDiagnostic() => NoDiagnosticAsync(
        """
        class C
        {
            void M<T>(dynamic d, T t, T other)
            {
                if (d == null) d = 1;
                if (t == null) t = other;
            }
        }
        """);

    [Fact]
    public Task AlreadyUsingOperator_NoDiagnostic() => NoDiagnosticAsync(
        """
        class C
        {
            void M(string? a)
            {
                a ??= "x";
            }
        }
        """);
}
