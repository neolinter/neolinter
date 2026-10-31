using Microsoft.CodeAnalysis.CSharp;

namespace Neolinter.Tests.InvertIf;

public class InvertIfTests
{
    // --- Exit contexts -------------------------------------------------------

    [Fact]
    public Task VoidMethod_ReturnsEarly() => Verify.FixAsync(
        """
        class C
        {
            void M(object o)
            {
                {|NL0001:if|} (o != null)
                {
                    A();
                    B();
                    A();
                }
            }

            void A() { }
            void B() { }
        }
        """,
        """
        class C
        {
            void M(object o)
            {
                if (o == null)
                {
                    return;
                }

                A();
                B();
                A();
            }

            void A() { }
            void B() { }
        }
        """);

    [Theory]
    [InlineData("foreach (var x in items)")]
    [InlineData("for (var i = 0; i < 3; i++)")]
    [InlineData("while (items.Length > 0)")]
    public Task Loop_Continues(string loop) => Verify.FixAsync(
        $$"""
        class C
        {
            void M(int[] items, bool c)
            {
                {{loop}}
                {
                    {|NL0001:if|} (c)
                    {
                        A();
                        A();
                        A();
                    }
                }
            }

            void A() { }
        }
        """,
        $$"""
        class C
        {
            void M(int[] items, bool c)
            {
                {{loop}}
                {
                    if (!c)
                    {
                        continue;
                    }

                    A();
                    A();
                    A();
                }
            }

            void A() { }
        }
        """);

    [Fact]
    public Task DoLoop_Continues() => Verify.FixAsync(
        """
        class C
        {
            void M(bool c)
            {
                do
                {
                    {|NL0001:if|} (c)
                    {
                        A();
                        A();
                        A();
                    }
                }
                while (c);
            }

            void A() { }
        }
        """,
        """
        class C
        {
            void M(bool c)
            {
                do
                {
                    if (!c)
                    {
                        continue;
                    }

                    A();
                    A();
                    A();
                }
                while (c);
            }

            void A() { }
        }
        """);

    [Fact]
    public Task AsyncTaskMethod_ReturnsEarly() => Verify.FixAsync(
        """
        using System.Threading.Tasks;

        class C
        {
            async Task M(bool c)
            {
                {|NL0001:if|} (c)
                {
                    await Task.Yield();
                    await Task.Yield();
                    await Task.Yield();
                }
            }
        }
        """,
        """
        using System.Threading.Tasks;

        class C
        {
            async Task M(bool c)
            {
                if (!c)
                {
                    return;
                }

                await Task.Yield();
                await Task.Yield();
                await Task.Yield();
            }
        }
        """);

    [Fact]
    public Task NonVoidMethod_RepeatsSimpleReturn() => Verify.FixAsync(
        """
        class C
        {
            int M(int[] items)
            {
                var count = 0;
                {|NL0001:if|} (items.Length > 0)
                {
                    count++;
                    count++;
                    count++;
                }
                return count;
            }
        }
        """,
        """
        class C
        {
            int M(int[] items)
            {
                var count = 0;
                if (items.Length <= 0)
                {
                    return count;
                }

                count++;
                count++;
                count++;
                return count;
            }
        }
        """);

    [Fact]
    public Task NonVoidMethod_ReturnWithCall_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            int M(bool c)
            {
                if (c)
                {
                    A();
                    A();
                    A();
                }
                return Compute();
            }

            void A() { }
            int Compute() => 0;
        }
        """);

    [Fact]
    public Task VoidLambda_IsItsOwnContext() => Verify.FixAsync(
        """
        using System;

        class C
        {
            void M(bool c)
            {
                Action a = () =>
                {
                    {|NL0001:if|} (c)
                    {
                        A();
                        A();
                        A();
                    }
                };
            }

            void A() { }
        }
        """,
        """
        using System;

        class C
        {
            void M(bool c)
            {
                Action a = () =>
                {
                    if (!c)
                    {
                        return;
                    }

                    A();
                    A();
                    A();
                };
            }

            void A() { }
        }
        """);

    [Fact]
    public Task LocalFunction_IsItsOwnContext() => Verify.FixAsync(
        """
        class C
        {
            int M(bool c)
            {
                Local();
                return 0;

                void Local()
                {
                    {|NL0001:if|} (c)
                    {
                        A();
                        A();
                        A();
                    }
                }
            }

            void A() { }
        }
        """,
        """
        class C
        {
            int M(bool c)
            {
                Local();
                return 0;

                void Local()
                {
                    if (!c)
                    {
                        return;
                    }

                    A();
                    A();
                    A();
                }
            }

            void A() { }
        }
        """);

    // --- if/else with an exit-only else --------------------------------------

    [Fact]
    public Task ElseThrow_IsMovedFirst() => Verify.FixAsync(
        """
        using System;

        class C
        {
            int M(bool active)
            {
                {|NL0001:if|} (active)
                {
                    A();
                }
                else
                {
                    throw new InvalidOperationException();
                }

                return 1;
            }

            void A() { }
        }
        """,
        """
        using System;

        class C
        {
            int M(bool active)
            {
                if (!active)
                {
                    throw new InvalidOperationException();
                }

                A();

                return 1;
            }

            void A() { }
        }
        """);

    [Fact]
    public Task ElseBreakWithoutBraces_InLoop() => Verify.FixAsync(
        """
        class C
        {
            void M(int[] items)
            {
                foreach (var x in items)
                {
                    {|NL0001:if|} (x == 0)
                    {
                        A();
                        A();
                    }
                    else break;
                    A();
                }
            }

            void A() { }
        }
        """,
        """
        class C
        {
            void M(int[] items)
            {
                foreach (var x in items)
                {
                    if (x != 0)
                    {
                        break;
                    }

                    A();
                    A();
                    A();
                }
            }

            void A() { }
        }
        """);

    [Fact]
    public Task ElseIfCascade_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(int x)
            {
                if (x == 0)
                {
                    A();
                    A();
                    A();
                }
                else if (x == 1)
                {
                    return;
                }
                else
                {
                    return;
                }
            }

            void A() { }
        }
        """);

    // --- Negative cases ------------------------------------------------------

    [Fact]
    public Task BelowThreshold_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(bool c)
            {
                if (c)
                {
                    A();
                    A();
                }
            }

            void A() { }
        }
        """);

    [Fact]
    public Task ThresholdFromEditorConfig() => Verify.FixAsync(
        """
        class C
        {
            void M(bool c)
            {
                {|NL0001:if|} (c)
                {
                    A();
                }
            }

            void A() { }
        }
        """,
        """
        class C
        {
            void M(bool c)
            {
                if (!c)
                {
                    return;
                }

                A();
            }

            void A() { }
        }
        """,
        editorConfig: "nl0001.min_statements = 1");

    [Fact]
    public Task StatementAfterIf_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(bool c)
            {
                if (c)
                {
                    A();
                    A();
                    A();
                }
                A();
            }

            void A() { }
        }
        """);

    [Fact]
    public Task NestedBlock_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(bool c)
            {
                try
                {
                    if (c)
                    {
                        A();
                        A();
                        A();
                    }
                }
                finally
                {
                }
            }

            void A() { }
        }
        """);

    [Fact]
    public Task Iterator_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        using System.Collections.Generic;

        class C
        {
            IEnumerable<int> M(bool c)
            {
                yield return 0;
                if (c)
                {
                    yield return 1;
                    yield return 2;
                    yield return 3;
                }
            }
        }
        """);

    [Fact]
    public Task NameClashWhenHoisting_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(int[] items, bool c)
            {
                foreach (var value in items)
                {
                }

                if (c)
                {
                    var value = 1;
                    A(value);
                    A(value);
                }
            }

            void A(int x) { }
        }
        """);

    [Fact]
    public Task UsingDeclarationBeforeFollowingCode_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        using System.IO;

        class C
        {
            void M(bool c)
            {
                if (c)
                {
                    using var stream = new MemoryStream();
                    stream.WriteByte(1);
                }
                else
                {
                    return;
                }

                A();
            }

            void A() { }
        }
        """);

    // --- Condition negation --------------------------------------------------

    [Theory]
    [InlineData("a == b", "a != b")]
    [InlineData("a != b", "a == b")]
    [InlineData("a < b", "a >= b")]
    [InlineData("a >= b", "a < b")]
    [InlineData("!(a == b)", "a == b")]
    [InlineData("!flag", "flag")]
    [InlineData("flag", "!flag")]
    [InlineData("flag && a > 0", "!(flag && a > 0)")]
    [InlineData("d < 1.0", "!(d < 1.0)")]
    [InlineData("n < 1", "!(n < 1)")]
    [InlineData("o is null", "o is not null")]
    [InlineData("o is not null", "o is null")]
    [InlineData("o is string", "o is not string")]
    [InlineData("Check()", "!Check()")]
    public Task Negation(string condition, string negated) => Verify.FixAsync(
        $$"""
        class C
        {
            void M(int a, int b, bool flag, double d, int? n, object o)
            {
                {|NL0001:if|} ({{condition}})
                {
                    A();
                    A();
                    A();
                }
            }

            bool Check() => true;
            void A() { }
        }
        """,
        $$"""
        class C
        {
            void M(int a, int b, bool flag, double d, int? n, object o)
            {
                if ({{negated}})
                {
                    return;
                }

                A();
                A();
                A();
            }

            bool Check() => true;
            void A() { }
        }
        """);

    [Fact]
    public Task CSharp8_KeepsNegatedIsNull() => Verify.FixAsync(
        """
        class C
        {
            void M(object o)
            {
                {|NL0001:if|} (o is null)
                {
                    A();
                    A();
                    A();
                }
            }

            void A() { }
        }
        """,
        """
        class C
        {
            void M(object o)
            {
                if (!(o is null))
                {
                    return;
                }

                A();
                A();
                A();
            }

            void A() { }
        }
        """,
        LanguageVersion.CSharp8);

    // --- Trivia --------------------------------------------------------------

    [Fact]
    public Task CommentsAndIndentationArePreserved() => Verify.FixAsync(
        """
        class C
        {
            void M(bool c)
            {
                // Only when c is set.
                {|NL0001:if|} (c)
                { // start
                    // first call
                    A();
                    A(); // second call
                    A(1,
                      2);
                    // done
                } // end
            }

            void A() { }
            void A(int x, int y) { }
        }
        """,
        """
        class C
        {
            void M(bool c)
            {
                // Only when c is set.
                if (!c)
                {
                    return;
                }

                // start
                // first call
                A();
                A(); // second call
                A(1,
                  2); // end
                // done
            }

            void A() { }
            void A(int x, int y) { }
        }
        """);

    [Fact]
    public Task SameLineBraceStyleIsKept() => Verify.FixAsync(
        """
        class C
        {
            void M(bool c) {
                {|NL0001:if|} (c) {
                    A();
                    A();
                    A();
                }
            }

            void A() { }
        }
        """,
        """
        class C
        {
            void M(bool c) {
                if (!c) {
                    return;
                }

                A();
                A();
                A();
            }

            void A() { }
        }
        """);

    [Fact]
    public Task TabIndentation() => Verify.FixAsync(
        "class C\n{\n\tvoid M(bool c)\n\t{\n\t\t{|NL0001:if|} (c)\n\t\t{\n\t\t\tA();\n\t\t\tA();\n\t\t\tA();\n\t\t}\n\t}\n\n\tvoid A() { }\n}\n",
        "class C\n{\n\tvoid M(bool c)\n\t{\n\t\tif (!c)\n\t\t{\n\t\t\treturn;\n\t\t}\n\n\t\tA();\n\t\tA();\n\t\tA();\n\t}\n\n\tvoid A() { }\n}\n");

    // --- Fix all -------------------------------------------------------------

    [Fact]
    public Task FixAll_InDocument() => Verify.FixAsync(
        """
        class C
        {
            void M1(bool c)
            {
                {|NL0001:if|} (c)
                {
                    A();
                    A();
                    A();
                }
            }

            void M2(bool c)
            {
                {|NL0001:if|} (!c)
                {
                    A();
                    A();
                    A();
                }
            }

            void A() { }
        }
        """,
        """
        class C
        {
            void M1(bool c)
            {
                if (!c)
                {
                    return;
                }

                A();
                A();
                A();
            }

            void M2(bool c)
            {
                if (c)
                {
                    return;
                }

                A();
                A();
                A();
            }

            void A() { }
        }
        """);
}
