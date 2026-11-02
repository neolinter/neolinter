using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.ElseAfterExit;
using Neolinter.Analyzers.InvertIf;
using Neolinter.Analyzers.MergeNestedIf;
using Neolinter.CodeFixes.ElseAfterExit;
using Neolinter.CodeFixes.InvertIf;

namespace Neolinter.Tests.ElseAfterExit;

/// <summary>NLR0009 tests. Several diagnostics in one source also exercise fix all.</summary>
public class ElseAfterExitTests
{
    private const string Disposable = "class R : System.IDisposable { public void Dispose() { } }\n";

    private static Task FixAsync(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<ElseAfterExitAnalyzer, ElseAfterExitCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        return test.RunAsync();
    }

    private static Task NoDiagnosticAsync(string source) => FixAsync(source, source);

    // Wraps statements in a method with a few helpers, so each test shows only the interesting lines.
    private static string Method(string body) =>
        "class C\n{\n    int field;\n\n    void M(bool c, bool d)\n    {\n" + body + "    }\n\n    void Run() { }\n}\n";

    // --- Reported shapes -------------------------------------------------------------------------

    [Fact]
    public Task Allman_BlockElse() => FixAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        Run();
                        Run();
                    }

            """),
        Method("""
                    if (c)
                    {
                        return;
                    }
                    Run();
                    Run();

            """));

    [Fact]
    public Task BraceOnTheSameLine_BlockElse() => FixAsync(
        Method("""
                    if (c) {
                        return;
                    } {|NLR0009:else|} {
                        Run();
                    }
                    Run();

            """),
        Method("""
                    if (c) {
                        return;
                    }
                    Run();
                    Run();

            """));

    [Fact]
    public Task SingleStatementElse_OnItsOwnLine() => FixAsync(
        Method("""
                    if (c)
                        return;
                    {|NLR0009:else|}
                        Run();

            """),
        Method("""
                    if (c)
                        return;
                    Run();

            """));

    [Fact]
    public Task EverythingOnOneLine() => FixAsync(
        Method("""
                    if (c) return; {|NLR0009:else|} Run();

            """),
        Method("""
                    if (c) return;
                    Run();

            """));

    [Fact]
    public Task EverythingOnOneLine_BlockElse() => FixAsync(
        Method("""
                    if (c) { return; } {|NLR0009:else|} { Run(); }

            """),
        Method("""
                    if (c) { return; }
                    Run();

            """));

    [Fact]
    public Task BothBranchesReturnValues() => FixAsync(
        """
        class C
        {
            int M(bool c)
            {
                if (c) { return 1; } {|NLR0009:else|} { return 2; }
            }
        }
        """,
        """
        class C
        {
            int M(bool c)
            {
                if (c) { return 1; }
                return 2;
            }
        }
        """);

    [Fact]
    public Task Throw() => FixAsync(
        Method("""
                    if (c)
                        throw new System.Exception();
                    {|NLR0009:else|}
                    {
                        Run();
                    }

            """),
        Method("""
                    if (c)
                        throw new System.Exception();
                    Run();

            """));

    [Fact]
    public Task Continue_InLoop() => FixAsync(
        Method("""
                    foreach (var i in new[] { 1, 2 })
                    {
                        if (i == 1)
                        {
                            continue;
                        }
                        {|NLR0009:else|}
                        {
                            Run();
                        }
                    }

            """),
        Method("""
                    foreach (var i in new[] { 1, 2 })
                    {
                        if (i == 1)
                        {
                            continue;
                        }
                        Run();
                    }

            """));

    [Fact]
    public Task Break_InLoop() => FixAsync(
        Method("""
                    while (c)
                    {
                        if (d) break; {|NLR0009:else|} Run();
                    }

            """),
        Method("""
                    while (c)
                    {
                        if (d) break;
                        Run();
                    }

            """));

    [Fact]
    public Task Goto() => FixAsync(
        Method("""
                    if (c)
                    {
                        goto done;
                    }
                    {|NLR0009:else|}
                    {
                        Run();
                    }
                done:
                    Run();

            """),
        Method("""
                    if (c)
                    {
                        goto done;
                    }
                    Run();
                done:
                    Run();

            """));

    [Fact]
    public Task YieldBreak() => FixAsync(
        """
        class C
        {
            System.Collections.Generic.IEnumerable<int> M(bool c)
            {
                if (c)
                {
                    yield break;
                }
                {|NLR0009:else|}
                {
                    yield return 1;
                }
            }
        }
        """,
        """
        class C
        {
            System.Collections.Generic.IEnumerable<int> M(bool c)
            {
                if (c)
                {
                    yield break;
                }
                yield return 1;
            }
        }
        """);

    [Fact]
    public Task ExitAfterOtherStatements() => FixAsync(
        Method("""
                    if (c)
                    {
                        Run();
                        Run();
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        Run();
                    }

            """),
        Method("""
                    if (c)
                    {
                        Run();
                        Run();
                        return;
                    }
                    Run();

            """));

    [Fact]
    public Task EveryPathOfANestedIfExits() => FixAsync(
        Method("""
                    if (c)
                    {
                        if (d)
                            return;
                        else if (c)
                            return;
                        else
                            throw new System.Exception();
                    }
                    {|NLR0009:else|}
                    {
                        Run();
                    }

            """),
        Method("""
                    if (c)
                    {
                        if (d)
                            return;
                        else if (c)
                            return;
                        else
                            throw new System.Exception();
                    }
                    Run();

            """));

    [Fact]
    public Task DeclaredLocal_WithoutClash() => FixAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        var x = 1;
                        field = x;
                    }

            """),
        Method("""
                    if (c)
                    {
                        return;
                    }
                    var x = 1;
                    field = x;

            """));

    [Fact]
    public Task ExpressionVariableOfTheCondition_StaysUsableInTheMovedStatements() => FixAsync(
        Method("""
                    if (!int.TryParse("1", out var n))
                    {
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        field = n;
                    }

            """),
        Method("""
                    if (!int.TryParse("1", out var n))
                    {
                        return;
                    }
                    field = n;

            """));

    [Fact]
    public Task UsingDeclaration_NothingFollows() => FixAsync(
        Disposable + Method("""
                    if (c)
                    {
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        using var r = new R();
                        r.Dispose();
                    }

            """),
        Disposable + Method("""
                    if (c)
                    {
                        return;
                    }
                    using var r = new R();
                    r.Dispose();

            """));

    [Fact]
    public Task UsingStatement_IsNotADeclaration() => FixAsync(
        Disposable + Method("""
                    if (c)
                    {
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        using (var r = new R())
                        {
                            r.Dispose();
                        }
                    }
                    Run();

            """),
        Disposable + Method("""
                    if (c)
                    {
                        return;
                    }
                    using (var r = new R())
                    {
                        r.Dispose();
                    }
                    Run();

            """));

    [Fact]
    public Task InABlockOfASwitchSection() => FixAsync(
        Method("""
                    switch (field)
                    {
                        case 1:
                        {
                            if (c)
                                break;
                            {|NLR0009:else|}
                                Run();
                            break;
                        }
                    }

            """),
        Method("""
                    switch (field)
                    {
                        case 1:
                        {
                            if (c)
                                break;
                            Run();
                            break;
                        }
                    }

            """));

    [Fact]
    public Task InALambda() => FixAsync(
        """
        using System;

        class C
        {
            Func<int, int> F = x =>
            {
                if (x > 0)
                {
                    return 1;
                }
                {|NLR0009:else|}
                {
                    return 2;
                }
            };
        }
        """,
        """
        using System;

        class C
        {
            Func<int, int> F = x =>
            {
                if (x > 0)
                {
                    return 1;
                }
                return 2;
            };
        }
        """);

    // --- Layout and comments -----------------------------------------------------------------------

    [Fact]
    public Task Comments_InsideTheElseAreKept() => FixAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        // explain

                        Run(); // after
                        // between
                        Run();
                    }

            """),
        Method("""
                    if (c)
                    {
                        return;
                    }
                    // explain

                    Run(); // after
                    // between
                    Run();

            """));

    [Fact]
    public Task Comments_AroundTheStatementsAreKept() => FixAsync(
        Method("""
                    // leave early
                    if (c) return; // exit
                    {|NLR0009:else|}
                        Run(); // work
                    // end

            """),
        Method("""
                    // leave early
                    if (c) return; // exit
                    Run(); // work
                    // end

            """));

    [Fact]
    public Task NestedStatements_MoveUpOneLevel() => FixAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        if (d)
                        {
                            Run();
                        }
                        var text = @"a
            b";
                    }

            """),
        Method("""
                    if (c)
                    {
                        return;
                    }
                    if (d)
                    {
                        Run();
                    }
                    var text = @"a
            b";

            """));

    [Fact]
    public Task Tabs() => FixAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        Run();
                    }

            """).Replace("    ", "\t"),
        Method("""
                    if (c)
                    {
                        return;
                    }
                    Run();

            """).Replace("    ", "\t"));

    [Fact]
    public Task WindowsLineEndings() => FixAsync(
        Method("""
                    if (c) { return; } {|NLR0009:else|} Run();

            """).Replace("\n", "\r\n"),
        Method("""
                    if (c) { return; }
                    Run();

            """).Replace("\n", "\r\n"));

    // --- Fix all -----------------------------------------------------------------------------------

    [Fact]
    public Task FixAll_SeveralInOneMethod() => FixAsync(
        Method("""
                    if (c)
                        return;
                    {|NLR0009:else|}
                        Run();

                    if (d)
                    {
                        return;
                    }
                    {|NLR0009:else|}
                    {
                        Run();
                        Run();
                    }

            """),
        Method("""
                    if (c)
                        return;
                    Run();

                    if (d)
                    {
                        return;
                    }
                    Run();
                    Run();

            """));

    [Fact]
    public Task FixAll_ElseInsideAnElse()
    {
        var test = new CSharpCodeFixTest<ElseAfterExitAnalyzer, ElseAfterExitCodeFixProvider, DefaultVerifier>
        {
            TestCode = Method("""
                        if (c)
                        {
                            return;
                        }
                        {|NLR0009:else|}
                        {
                            if (d)
                            {
                                return;
                            }
                            {|NLR0009:else|}
                            {
                                Run();
                            }
                        }

                """),
            FixedCode = Method("""
                        if (c)
                        {
                            return;
                        }
                        if (d)
                        {
                            return;
                        }
                        Run();

                """),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            NumberOfFixAllIterations = 2,
            NumberOfIncrementalIterations = 2,
        };

        return test.RunAsync();
    }

    [Fact]
    public Task FixAll_InDifferentMethods() => FixAsync(
        """
        class C
        {
            int A(bool c)
            {
                if (c) { return 1; } {|NLR0009:else|} { return 2; }
            }

            int B(bool c)
            {
                if (c)
                {
                    return 1;
                }
                {|NLR0009:else|}
                {
                    return 3;
                }
            }
        }
        """,
        """
        class C
        {
            int A(bool c)
            {
                if (c) { return 1; }
                return 2;
            }

            int B(bool c)
            {
                if (c)
                {
                    return 1;
                }
                return 3;
            }
        }
        """);

    // --- Not reported ----------------------------------------------------------------------------------

    [Fact]
    public Task NotReported_ElseIfChain() => NoDiagnosticAsync(
        Method("""
                    if (c)
                        return;
                    else if (d)
                        Run();
                    else
                        Run();

            """));

    [Fact]
    public Task NotReported_ElseOfAnElseIf() => NoDiagnosticAsync(
        Method("""
                    if (c)
                        Run();
                    else if (d)
                        return;
                    else
                        Run();

            """));

    [Fact]
    public Task NotReported_BranchDoesNotAlwaysExit() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        if (d)
                        {
                            return;
                        }
                    }
                    else
                    {
                        Run();
                    }

                    if (d)
                    {
                        Run();
                    }
                    else
                    {
                        Run();
                    }

            """));

    [Fact]
    public Task NotReported_NoElse() => NoDiagnosticAsync(
        Method("""
                    if (c)
                        return;
                    Run();

            """));

    [Fact]
    public Task NotReported_EmptyElse() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    else
                    {
                    }

            """));

    [Fact]
    public Task NotReported_DanglingElse() => NoDiagnosticAsync(
        Method("""
                    if (c)
                        if (d)
                            return;
                        else
                            Run();

            """));

    [Fact]
    public Task NotReported_BodyWithoutBraces() => NoDiagnosticAsync(
        Method("""
                    while (c)
                        if (d)
                            break;
                        else
                            Run();

            """));

    [Fact]
    public Task NotReported_DirectlyInASwitchSection() => NoDiagnosticAsync(
        Method("""
                    switch (field)
                    {
                        case 1:
                            if (c)
                                break;
                            else
                                Run();
                            break;
                    }

            """));

    [Fact]
    public Task NotReported_Directive() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    else
                    {
            #if DEBUG
                        Run();
            #endif
                    }

            """));

    [Fact]
    public Task NotReported_CommentOnTheElse() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    // otherwise
                    else
                    {
                        Run();
                    }

            """));

    [Fact]
    public Task NotReported_CommentNextToTheBraces() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    else // the rest
                    {
                        Run();
                    }

            """));

    [Fact]
    public Task NotReported_CommentBeforeTheClosingBrace() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    else
                    {
                        Run();
                        // nothing else
                    }

            """));

    [Fact]
    public Task NotReported_IfDoesNotStartItsLine() => NoDiagnosticAsync(
        Method("""
                    Run(); if (c) return; else Run();

            """));

    [Fact]
    public Task NotReported_LaterStatementOfTheElseSharesALine() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    else
                    {
                        Run(); Run();
                    }

            """));

    [Fact]
    public Task NotReported_SingleExitAfterALongerBranch_IsLeftToNlr0001() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        Run();
                        Run();
                        return;
                    }
                    else
                    {
                        return;
                    }

            """));

    // --- Scope: names declared in the else ----------------------------------------------------------------

    [Fact]
    public Task NotReported_NameDeclaredInTheIfBranch() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        var x = 0;
                        field = x;
                        return;
                    }
                    else
                    {
                        var x = 1;
                        field = x;
                    }

            """));

    [Fact]
    public Task NotReported_NameDeclaredInAnEarlierSiblingScope() => NoDiagnosticAsync(
        Method("""
                    for (var i = 0; i < 2; i++)
                    {
                        Run();
                    }

                    if (c)
                    {
                        return;
                    }
                    else
                    {
                        var i = 1;
                        field = i;
                    }

            """));

    [Fact]
    public Task NotReported_LaterCodeUsesAFieldWithTheSameName() => NoDiagnosticAsync(
        Method("""
                    if (c)
                    {
                        return;
                    }
                    else
                    {
                        var field = 1;
                        Run();
                    }
                    this.field = 2;
                    field = 3;

            """));

    [Fact]
    public Task NotReported_UsingDeclarationWhenCodeFollows() => NoDiagnosticAsync(
        Disposable + Method("""
                    if (c)
                    {
                        return;
                    }
                    else
                    {
                        using var r = new R();
                        r.Dispose();
                    }
                    Run();

            """));

    // --- Interaction with NLR0001 and NLR0008 ----------------------------------------------------------------

    private sealed class AllRulesFix : CSharpCodeFixTest<ElseAfterExitAnalyzer, ElseAfterExitCodeFixProvider, DefaultVerifier>
    {
        public AllRulesFix(string source, string fixedSource)
        {
            TestCode = source;
            FixedCode = fixedSource;
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        }

        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
            base.GetDiagnosticAnalyzers().Append(new InvertIfAnalyzer()).Append(new MergeNestedIfAnalyzer());
    }

    private sealed class InvertIfWithNlr0009 : CSharpCodeFixTest<InvertIfAnalyzer, InvertIfCodeFixProvider, DefaultVerifier>
    {
        public InvertIfWithNlr0009(string source, string fixedSource)
        {
            TestCode = source;
            FixedCode = fixedSource;
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        }

        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() =>
            base.GetDiagnosticAnalyzers().Append(new ElseAfterExitAnalyzer());
    }

    [Fact]
    public Task Nlr0001_ReportsAShortExitInTheElse_Nlr0009StaysSilent_AndItsFixLeavesNothingToReport() =>
        new InvertIfWithNlr0009(
            Method("""
                        {|NLR0001:if|} (c)
                        {
                            Run();
                            Run();
                            Run();
                        }
                        else
                        {
                            return;
                        }

                """),
            Method("""
                        if (!c)
                        {
                            return;
                        }

                        Run();
                        Run();
                        Run();

                """)).RunAsync();

    [Fact]
    public Task Nlr0009_ReportsALongElse_Nlr0001StaysSilent_AndTheFixLeavesNothingToReport() =>
        new AllRulesFix(
            Method("""
                        if (c)
                        {
                            return;
                        }
                        {|NLR0009:else|}
                        {
                            Run();
                            Run();
                            Run();
                        }

                """),
            Method("""
                        if (c)
                        {
                            return;
                        }
                        Run();
                        Run();
                        Run();

                """)).RunAsync();

    [Fact]
    public Task Nlr0008_NestedIfWithElseIsNotMergeable_AndStaysSoAfterTheFix() =>
        new AllRulesFix(
            Method("""
                        if (c)
                        {
                            if (d)
                            {
                                return;
                            }
                            {|NLR0009:else|}
                            {
                                Run();
                            }
                        }

                """),
            Method("""
                        if (c)
                        {
                            if (d)
                            {
                                return;
                            }
                            Run();
                        }

                """)).RunAsync();
}
