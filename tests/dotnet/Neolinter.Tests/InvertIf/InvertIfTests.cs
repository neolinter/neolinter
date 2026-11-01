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
                {|NLR0001:if|} (o != null)
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
                    {|NLR0001:if|} (c)
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
                    {|NLR0001:if|} (c)
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
                {|NLR0001:if|} (c)
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
                {|NLR0001:if|} (items.Length > 0)
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
                    {|NLR0001:if|} (c)
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
                    {|NLR0001:if|} (c)
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
                {|NLR0001:if|} (active)
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
                    {|NLR0001:if|} (x == 0)
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
                {|NLR0001:if|} (c)
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
        editorConfig: "nlr0001.min_statements = 1");

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

    // --- Iterators: yield break is an exit --------------------------------------

    [Fact]
    public Task Iterator_LeavesWithYieldBreak() => Verify.FixAsync(
        """
        using System.Collections.Generic;

        class C
        {
            IEnumerable<int> M(bool c)
            {
                yield return 0;
                {|NLR0001:if|} (c)
                {
                    yield return 1;
                    yield return 2;
                    yield return 3;
                }
            }
        }
        """,
        """
        using System.Collections.Generic;

        class C
        {
            IEnumerable<int> M(bool c)
            {
                yield return 0;
                if (!c)
                {
                    yield break;
                }

                yield return 1;
                yield return 2;
                yield return 3;
            }
        }
        """);

    [Fact]
    public Task Iterator_ElseYieldBreak_IsMovedFirst() => Verify.FixAsync(
        """
        using System.Collections.Generic;

        class C
        {
            IEnumerable<int> M(bool c)
            {
                {|NLR0001:if|} (c)
                {
                    yield return 1;
                }
                else
                {
                    yield break;
                }

                yield return 2;
            }
        }
        """,
        """
        using System.Collections.Generic;

        class C
        {
            IEnumerable<int> M(bool c)
            {
                if (!c)
                {
                    yield break;
                }

                yield return 1;

                yield return 2;
            }
        }
        """);

    [Fact]
    public Task Iterator_StatementAfterIf_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        using System.Collections.Generic;

        class C
        {
            IEnumerable<int> M(bool c)
            {
                if (c)
                {
                    yield return 1;
                    yield return 2;
                    yield return 3;
                }

                yield return 4;
            }
        }
        """);

    [Fact]
    public Task Iterator_BelowThreshold_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        using System.Collections.Generic;

        class C
        {
            IEnumerable<int> M(bool c)
            {
                if (c)
                {
                    yield return 1;
                }
            }
        }
        """);

    [Fact]
    public Task YieldInLocalFunction_DoesNotMakeOuterMethodAnIterator() => Verify.FixAsync(
        """
        using System.Collections.Generic;

        class C
        {
            void M(bool c)
            {
                IEnumerable<int> Local()
                {
                    yield break;
                }

                {|NLR0001:if|} (c)
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
        using System.Collections.Generic;

        class C
        {
            void M(bool c)
            {
                IEnumerable<int> Local()
                {
                    yield break;
                }

                if (!c)
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
                {|NLR0001:if|} ({{condition}})
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
                {|NLR0001:if|} (o is null)
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
                {|NLR0001:if|} (c)
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
                {|NLR0001:if|} (c) {
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
        "class C\n{\n\tvoid M(bool c)\n\t{\n\t\t{|NLR0001:if|} (c)\n\t\t{\n\t\t\tA();\n\t\t\tA();\n\t\t\tA();\n\t\t}\n\t}\n\n\tvoid A() { }\n}\n",
        "class C\n{\n\tvoid M(bool c)\n\t{\n\t\tif (!c)\n\t\t{\n\t\t\treturn;\n\t\t}\n\n\t\tA();\n\t\tA();\n\t\tA();\n\t}\n\n\tvoid A() { }\n}\n");

    // --- Fix all -------------------------------------------------------------

    [Fact]
    public Task FixAll_InDocument() => Verify.FixAsync(
        """
        class C
        {
            void M1(bool c)
            {
                {|NLR0001:if|} (c)
                {
                    A();
                    A();
                    A();
                }
            }

            void M2(bool c)
            {
                {|NLR0001:if|} (!c)
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

    // --- else if cascades whose final else only exits -------------------------

    [Fact]
    public Task ElseIfCascade_FinalElseReturns_GuardGoesFirst() => Verify.FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0001:if|} (a)
                {
                    A();
                }
                else if (b)
                {
                    B();
                }
                else
                {
                    return;
                }

                C2();
            }

            void A() { }
            void B() { }
            void C2() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (!a && !b)
                {
                    return;
                }

                if (a)
                {
                    A();
                }
                else
                {
                    B();
                }

                C2();
            }

            void A() { }
            void B() { }
            void C2() { }
        }
        """);

    [Fact]
    public Task ElseIfCascade_ThreeBranchesInLoop_FinalElseContinues() => Verify.FixAsync(
        """
        class C
        {
            void M(int[] items)
            {
                foreach (var x in items)
                {
                    {|NLR0001:if|} (x == 0)
                    {
                        A();
                    }
                    else if (x == 1)
                    {
                        B();
                    }
                    else if (x > 5)
                    {
                        A();
                        B();
                    }
                    else continue;

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
            void M(int[] items)
            {
                foreach (var x in items)
                {
                    if (x != 0 && x != 1 && x <= 5)
                    {
                        continue;
                    }

                    if (x == 0)
                    {
                        A();
                    }
                    else if (x == 1)
                    {
                        B();
                    }
                    else
                    {
                        A();
                        B();
                    }

                    A();
                }
            }

            void A() { }
            void B() { }
        }
        """);

    [Fact]
    public Task ElseIfCascade_KAndRBraces_AreKept() => Verify.FixAsync(
        """
        class C
        {
            void M(bool a, bool b) {
                {|NLR0001:if|} (a || b) {
                    A();
                } else if (b) {
                    A();
                } else {
                    throw new System.Exception();
                }

                A();
            }

            void A() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b) {
                if (!(a || b) && !b) {
                    throw new System.Exception();
                }

                if (a || b) {
                    A();
                } else {
                    A();
                }

                A();
            }

            void A() { }
        }
        """);

    [Fact]
    public Task ElseIfCascade_YieldBreakInFinalElse() => Verify.FixAsync(
        """
        using System.Collections.Generic;

        class C
        {
            IEnumerable<int> M(bool a, bool b)
            {
                {|NLR0001:if|} (a)
                {
                    yield return 1;
                }
                else if (b)
                {
                    yield return 2;
                }
                else
                {
                    yield break;
                }

                yield return 3;
            }
        }
        """,
        """
        using System.Collections.Generic;

        class C
        {
            IEnumerable<int> M(bool a, bool b)
            {
                if (!a && !b)
                {
                    yield break;
                }

                if (a)
                {
                    yield return 1;
                }
                else
                {
                    yield return 2;
                }

                yield return 3;
            }
        }
        """);

    [Fact]
    public Task ElseIfCascade_FixAllWithOtherIf() => Verify.FixAsync(
        """
        class C
        {
            void M1(bool a, bool b)
            {
                {|NLR0001:if|} (a) { A(); }
                else if (b) { A(); }
                else { return; }

                A();
            }

            void M2(bool c)
            {
                {|NLR0001:if|} (c)
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
            void M1(bool a, bool b)
            {
                if (!a && !b) {
                    return;
                }

                if (a) { A(); }
                else { A(); }

                A();
            }

            void M2(bool c)
            {
                if (!c)
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

    [Fact]
    public Task ElseIfCascade_BranchThatOnlyExits_NoDiagnostic() => Verify.NoDiagnosticAsync(
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

    [Fact]
    public Task ElseIfCascade_ConditionWithCall_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a)
            {
                if (a)
                {
                    A();
                }
                else if (Check())
                {
                    A();
                }
                else
                {
                    return;
                }

                A();
            }

            bool Check() => true;
            void A() { }
        }
        """);

    [Fact]
    public Task ElseIfCascade_ConditionDeclaresVariable_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(object o)
            {
                if (o is int i)
                {
                    A(i);
                }
                else if (o is string)
                {
                    A(0);
                }
                else
                {
                    return;
                }

                A(1);
            }

            void A(int x) { }
        }
        """);

    [Fact]
    public Task ElseIfCascade_UserDefinedOperator_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        struct V
        {
            public static bool operator ==(V l, V r) => true;
            public static bool operator !=(V l, V r) => false;
            public override bool Equals(object o) => true;
            public override int GetHashCode() => 0;
        }

        class C
        {
            void M(bool a, V v, V w)
            {
                if (a)
                {
                    A();
                }
                else if (v == w)
                {
                    A();
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

    [Fact]
    public Task ElseIfCascade_FinalElseIsNotOnlyAnExit_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
                    A();
                }
                else if (b)
                {
                    A();
                }
                else
                {
                    A();
                    return;
                }

                A();
            }

            void A() { }
        }
        """);

    [Fact]
    public Task ElseIfCascade_WithoutFinalElse_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
                    A();
                }
                else if (b)
                {
                    A();
                }

                A();
            }

            void A() { }
        }
        """);

    [Fact]
    public Task ElseIfCascade_CommentBetweenBranches_NoDiagnostic() => Verify.NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
                    A();
                }
                // second case
                else if (b)
                {
                    A();
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

    // --- nlr0001.simplify_logical_operators ------------------------------------

    [Theory]
    [InlineData("a && b", "!a || !b")]
    [InlineData("a || b", "!a && !b")]
    [InlineData("a && !b", "!a || b")]
    [InlineData("a && n > 0", "!a || n <= 0")]
    [InlineData("a && (b || c)", "!a || !b && !c")]
    [InlineData("(a || b) && c", "!a && !b || !c")]
    [InlineData("!(a && b)", "a && b")]
    [InlineData("a == b", "a != b")]
    public Task SimplifyLogicalOperators_On(string condition, string negated) => Verify.FixAsync(
        $$"""
        class C
        {
            void M(bool a, bool b, bool c, int n)
            {
                {|NLR0001:if|} ({{condition}})
                {
                    A();
                    A();
                    A();
                }
            }

            void A() { }
        }
        """,
        $$"""
        class C
        {
            void M(bool a, bool b, bool c, int n)
            {
                if ({{negated}})
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
        editorConfig: "nlr0001.simplify_logical_operators = true");

    [Theory]
    [InlineData("a && b", "!(a && b)")]
    [InlineData("a || b", "!(a || b)")]
    public Task SimplifyLogicalOperators_OffByDefault(string condition, string negated) => Verify.FixAsync(
        $$"""
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0001:if|} ({{condition}})
                {
                    A();
                    A();
                    A();
                }
            }

            void A() { }
        }
        """,
        $$"""
        class C
        {
            void M(bool a, bool b)
            {
                if ({{negated}})
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

    [Fact]
    public Task SimplifyLogicalOperators_InvalidValueMeansOff() => Verify.FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0001:if|} (a && b)
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
            void M(bool a, bool b)
            {
                if (!(a && b))
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
        editorConfig: "nlr0001.simplify_logical_operators = maybe");

    [Fact]
    public Task SimplifyLogicalOperators_UserDefinedOperators_AreLeftAlone() => Verify.FixAsync(
        """
        struct Flag
        {
            public static Flag operator &(Flag l, Flag r) => l;
            public static Flag operator |(Flag l, Flag r) => l;
            public static bool operator true(Flag f) => true;
            public static bool operator false(Flag f) => false;
            public static bool operator !(Flag f) => false;
        }

        class C
        {
            void M(Flag a, Flag b)
            {
                {|NLR0001:if|} (a && b)
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
        struct Flag
        {
            public static Flag operator &(Flag l, Flag r) => l;
            public static Flag operator |(Flag l, Flag r) => l;
            public static bool operator true(Flag f) => true;
            public static bool operator false(Flag f) => false;
            public static bool operator !(Flag f) => false;
        }

        class C
        {
            void M(Flag a, Flag b)
            {
                if (!(a && b))
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
        editorConfig: "nlr0001.simplify_logical_operators = true");

    [Fact]
    public Task SimplifyLogicalOperators_AppliesToCascadeGuard() => Verify.FixAsync(
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                {|NLR0001:if|} (a || b)
                {
                    A();
                }
                else if (c)
                {
                    A();
                }
                else
                {
                    return;
                }

                A();
            }

            void A() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                if (!a && !b && !c)
                {
                    return;
                }

                if (a || b)
                {
                    A();
                }
                else
                {
                    A();
                }

                A();
            }

            void A() { }
        }
        """,
        editorConfig: "nlr0001.simplify_logical_operators = true");

    // --- nlr0001.guard_braces ----------------------------------------------------

    [Fact]
    public Task GuardBraces_Never_ReturnWithoutBraces() => Verify.FixAsync(
        """
        class C
        {
            void M(bool c)
            {
                {|NLR0001:if|} (c)
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
            void M(bool c)
            {
                if (!c)
                    return;

                A();
                A();
                A();
            }

            void A() { }
        }
        """,
        editorConfig: "nlr0001.guard_braces = never");

    [Fact]
    public Task GuardBraces_Never_ElseThrowWithoutBraces() => Verify.FixAsync(
        """
        using System;

        class C
        {
            int M(bool active)
            {
                {|NLR0001:if|} (active)
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
                    throw new InvalidOperationException();

                A();

                return 1;
            }

            void A() { }
        }
        """,
        editorConfig: "nlr0001.guard_braces = never");

    [Fact]
    public Task GuardBraces_Never_ElseOnSameLine() => Verify.FixAsync(
        """
        class C
        {
            void M(int[] items)
            {
                foreach (var x in items)
                {
                    {|NLR0001:if|} (x == 0)
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
                        break;

                    A();
                    A();
                    A();
                }
            }

            void A() { }
        }
        """,
        editorConfig: "nlr0001.guard_braces = never");

    [Fact]
    public Task GuardBraces_Never_CascadeGuard() => Verify.FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0001:if|} (a)
                {
                    A();
                }
                else if (b)
                {
                    A();
                }
                else
                {
                    return;
                }

                A();
            }

            void A() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (!a && !b)
                    return;

                if (a)
                {
                    A();
                }
                else
                {
                    A();
                }

                A();
            }

            void A() { }
        }
        """,
        editorConfig: "nlr0001.guard_braces = never");

    [Fact]
    public Task GuardBraces_Never_KeepsBracesAroundComments() => Verify.FixAsync(
        """
        class C
        {
            void M(bool c)
            {
                {|NLR0001:if|} (c)
                {
                    A();
                }
                else
                { // nothing to do
                    return;
                }

                A();
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
                { // nothing to do
                    return;
                }

                A();

                A();
            }

            void A() { }
        }
        """,
        editorConfig: "nlr0001.guard_braces = never");

    [Theory]
    [InlineData("nlr0001.guard_braces = always")]
    [InlineData("nlr0001.guard_braces = sometimes")]
    public Task GuardBraces_AlwaysOrInvalid_KeepsBraces(string editorConfig) => Verify.FixAsync(
        """
        class C
        {
            void M(bool c)
            {
                {|NLR0001:if|} (c)
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
            void M(bool c)
            {
                if (!c)
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
        editorConfig: editorConfig);
}
