using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.BooleanComparison;
using Neolinter.CodeFixes.BooleanComparison;

namespace Neolinter.Tests.BooleanComparison;

/// <summary>NLR0005 tests. Several diagnostics in one source also exercise fix all.</summary>
public class BooleanComparisonTests
{
    private static Task FixAsync(string source, string fixedSource) => Run(source, fixedSource);

    private static Task NoDiagnosticAsync(string source) => Run(source, source);

    private static Task Run(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<BooleanComparisonAnalyzer, BooleanComparisonCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        return test.RunAsync();
    }

    // --- Reported ------------------------------------------------------------

    [Theory]
    [InlineData("x == true", "x")]
    [InlineData("true == x", "x")]
    [InlineData("x != false", "x")]
    [InlineData("false != x", "x")]
    [InlineData("x == false", "!x")]
    [InlineData("false == x", "!x")]
    [InlineData("x != true", "!x")]
    [InlineData("true != x", "!x")]
    public Task AllSixForms(string comparison, string expected) => FixAsync(
        $$"""
        class C
        {
            bool M(bool x) => {|NLR0005:{{comparison}}|};
        }
        """,
        $$"""
        class C
        {
            bool M(bool x) => {{expected}};
        }
        """);

    [Fact]
    public Task FixAll_LayoutAndCommentsAroundAreKept() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                // keep me
                if ({|NLR0005:a == true|}) // trailing
                {
                    var c = {|NLR0005:b   !=   true|};
                }

                var d = {|NLR0005:false ==
                    b|};
            }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                // keep me
                if (a) // trailing
                {
                    var c = !b;
                }

                var d = !b;
            }
        }
        """);

    [Fact]
    public Task ParenthesesOnlyWhenNeeded() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b, bool c, bool? n, int i, object o)
            {
                var r1 = a && {|NLR0005:b == true|};
                var r2 = {|NLR0005:(a && b) == false|};
                var r3 = a && {|NLR0005:b == false|};
                var r4 = {|NLR0005:(n ?? b) == true|};
                var r5 = {|NLR0005:i < 3 == false|};
                var r6 = {|NLR0005:o is string == true|};
                var r7 = {|NLR0005:o is string == false|};
                var r8 = {|NLR0005:(a ? b : c) == false|};
                var r9 = {|NLR0005:(bool)o == false|};
                var r10 = {|NLR0005:!(a && b) == false|};
                var r11 = {|NLR0005:!a == true|};
                var r12 = {|NLR0005:a == b == false|};
            }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b, bool c, bool? n, int i, object o)
            {
                var r1 = a && b;
                var r2 = !(a && b);
                var r3 = a && !b;
                var r4 = (n ?? b);
                var r5 = !(i < 3);
                var r6 = o is string;
                var r7 = !(o is string);
                var r8 = !(a ? b : c);
                var r9 = !(bool)o;
                var r10 = (a && b);
                var r11 = !a;
                var r12 = !(a == b);
            }
        }
        """);

    [Fact]
    public Task AwaitAndLambda() => FixAsync(
        """
        using System;
        using System.Threading.Tasks;

        class C
        {
            async Task<bool> M(Task<bool> t, Func<bool, bool> f)
            {
                var a = {|NLR0005:await t == false|};
                Func<bool, bool> g = x => {|NLR0005:x != true|};
                return {|NLR0005:f(a) == true|};
            }
        }
        """,
        """
        using System;
        using System.Threading.Tasks;

        class C
        {
            async Task<bool> M(Task<bool> t, Func<bool, bool> f)
            {
                var a = !await t;
                Func<bool, bool> g = x => !x;
                return f(a);
            }
        }
        """);

    [Fact]
    public Task NestedComparisons_FixAll() => FixAsync(
        """
        class C
        {
            bool M(bool a) => {|NLR0005:({|NLR0005:a == true|}) == true|};
        }
        """,
        """
        class C
        {
            bool M(bool a) => (a);
        }
        """);

    // --- Not reported --------------------------------------------------------

    [Fact]
    public Task NullableBool() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool? x)
            {
                var a = x == true;
                var b = x == false;
                var c = true != x;
                var d = x != false;
            }
        }
        """);

    [Fact]
    public Task CustomTypesAndUserDefinedOperators() => NoDiagnosticAsync(
        """
        struct Flag
        {
            public static implicit operator bool(Flag f) => true;
        }

        struct Tri
        {
            public static bool operator ==(Tri a, bool b) => true;
            public static bool operator !=(Tri a, bool b) => false;
            public override bool Equals(object? o) => true;
            public override int GetHashCode() => 0;
        }

        class C
        {
            void M(Flag f, Tri t, dynamic d)
            {
                var a = f == true;
                var b = t == false;
                var c = d == true;
            }
        }
        """);

    [Fact]
    public Task OtherComparisons() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b, int i)
            {
                var r1 = a == b;
                var r2 = i == 1;
                var r3 = true == false;
                var r4 = true != true;
                var r5 = a & true;
            }
        }
        """);

    [Fact]
    public Task CommentInsideTheComparison() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a)
            {
                var r1 = a /* deliberate */ == true;
                var r2 = a == // deliberate
                    true;
            }
        }
        """);
}
