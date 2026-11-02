using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.MergeNestedIf;
using Neolinter.CodeFixes.MergeNestedIf;

namespace Neolinter.Tests.MergeNestedIf;

/// <summary>NLR0008 tests. Several diagnostics in one source also exercise fix all.</summary>
public class MergeNestedIfTests
{
    private static Task FixAsync(string source, string fixedSource)
    {
        var test = new CSharpCodeFixTest<MergeNestedIfAnalyzer, MergeNestedIfCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        return test.RunAsync();
    }

    private static Task NoDiagnosticAsync(string source) => FixAsync(source, source);

    // --- Reported shapes -------------------------------------------------------------------------

    [Fact]
    public Task Braces_AllmanStyle() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0008:if|} (a)
                {
                    if (b)
                    {
                        Run();
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a && b)
                {
                    Run();
                    Run();
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task Braces_BraceOnTheSameLine() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0008:if|} (a) {
                    if (b) {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a && b) {
                    Run();
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task NoBraces_OnSeparateLines() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0008:if|} (a)
                    if (b)
                        Run();
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a && b)
                    Run();
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task NoBraces_OnOneLine() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0008:if|} (a) if (b) Run();
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a && b) Run();
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task OuterBraces_InnerWithoutBraces() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0008:if|} (a)
                {
                    if (b)
                        Run();
                }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a && b)
                    Run();
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task OuterBraces_InnerWithoutBraces_OneLine() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0008:if|} (a) { if (b) Run(); }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a && b) Run();
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task OuterWithoutBraces_InnerWithBraces() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                {|NLR0008:if|} (a)
                    if (b)
                    {
                        Run();
                    }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a && b)
                {
                    Run();
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task Chain_ReportedOnceOnTheOutermost() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b, bool c, bool d)
            {
                {|NLR0008:if|} (a)
                {
                    if (b)
                    {
                        if (c)
                            if (d)
                            {
                                Run();
                            }
                    }
                }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b, bool c, bool d)
            {
                if (a && b && c && d)
                {
                    Run();
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task ChainStopsAtAnInnerElse() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                {|NLR0008:if|} (a)
                {
                    if (b)
                    {
                        if (c)
                        {
                            Run();
                        }
                        else
                        {
                            Run();
                        }
                    }
                }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                if (a && b)
                {
                    if (c)
                    {
                        Run();
                    }
                    else
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task ElseIf_OuterIsTheElseIf() => FixAsync(
        """
        class C
        {
            void M(bool x, bool a, bool b)
            {
                if (x)
                {
                    Run();
                }
                else {|NLR0008:if|} (a)
                {
                    if (b)
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool x, bool a, bool b)
            {
                if (x)
                {
                    Run();
                }
                else if (a && b)
                {
                    Run();
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task InsideALoopAndALambda() => FixAsync(
        """
        using System;

        class C
        {
            void M(bool a, bool b)
            {
                foreach (var i in new[] { 1, 2 })
                {
                    {|NLR0008:if|} (a)
                    {
                        if (b)
                        {
                            Run();
                        }
                    }
                }

                Action f = () =>
                {
                    {|NLR0008:if|} (a)
                        if (b)
                            Run();
                };
            }

            void Run() { }
        }
        """,
        """
        using System;

        class C
        {
            void M(bool a, bool b)
            {
                foreach (var i in new[] { 1, 2 })
                {
                    if (a && b)
                    {
                        Run();
                    }
                }

                Action f = () =>
                {
                    if (a && b)
                        Run();
                };
            }

            void Run() { }
        }
        """);

    // --- Comments and layout -----------------------------------------------------------------------

    [Fact]
    public Task Comments_AroundAndInsideTheBodyAreKept() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                // explain the outer one
                {|NLR0008:if|} (a)
                {
                    if (b) // why b
                    {
                        // inside
                        Run(); // after
                    }
                } // end of outer
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b)
            {
                // explain the outer one
                if (a && b) // why b
                {
                    // inside
                    Run(); // after
                } // end of outer
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task MultiLineStringInTheBody_IsNotReindented()
    {
        // The verbatim string keeps its text: only whitespace that starts a line of code is moved.
        return FixAsync(
            "class C\n{\n    string M(bool a, bool b)\n    {\n        {|NLR0008:if|} (a)\n        {\n            if (b)\n            {\n                return @\"x\n            y\";\n            }\n        }\n\n        return \"\";\n    }\n}\n",
            "class C\n{\n    string M(bool a, bool b)\n    {\n        if (a && b)\n        {\n            return @\"x\n            y\";\n        }\n\n        return \"\";\n    }\n}\n");
    }

    [Fact]
    public Task Tabs_AreHandled() => FixAsync(
        "class C\n{\n\tvoid M(bool a, bool b)\n\t{\n\t\t{|NLR0008:if|} (a)\n\t\t{\n\t\t\tif (b)\n\t\t\t{\n\t\t\t\tRun();\n\t\t\t}\n\t\t}\n\t}\n\n\tvoid Run() { }\n}\n",
        "class C\n{\n\tvoid M(bool a, bool b)\n\t{\n\t\tif (a && b)\n\t\t{\n\t\t\tRun();\n\t\t}\n\t}\n\n\tvoid Run() { }\n}\n");

    [Fact]
    public Task CrLf_IsKept() => FixAsync(
        "class C\r\n{\r\n    void M(bool a, bool b)\r\n    {\r\n        {|NLR0008:if|} (a)\r\n        {\r\n            if (b)\r\n            {\r\n                Run();\r\n            }\r\n        }\r\n    }\r\n\r\n    void Run() { }\r\n}\r\n",
        "class C\r\n{\r\n    void M(bool a, bool b)\r\n    {\r\n        if (a && b)\r\n        {\r\n            Run();\r\n        }\r\n    }\r\n\r\n    void Run() { }\r\n}\r\n");

    // --- Precedence ------------------------------------------------------------------------------

    [Theory]
    [InlineData("a || c", "b", "(a || c) && b")]
    [InlineData("a", "b || c", "a && (b || c)")]
    [InlineData("a && c", "b", "a && c && b")]
    [InlineData("a", "b && c", "a && b && c")]
    [InlineData("a == c", "b != c", "a == c && b != c")]
    [InlineData("!a", "!b", "!a && !b")]
    [InlineData("a ? b : c", "b", "(a ? b : c) && b")]
    [InlineData("a", "b ? a : c", "a && (b ? a : c)")]
    [InlineData("a | c", "b ^ c", "a | c && b ^ c")]
    [InlineData("(a || c)", "b", "(a || c) && b")]
    [InlineData("o is string", "o is not null", "o is string && o is not null")]
    [InlineData("o is int or long", "o is { }", "o is int or long && o is { }")]
    [InlineData("o switch { 1 => true, _ => false }", "b", "(o switch { 1 => true, _ => false }) && b")]
    [InlineData("a", "o switch { 1 => true, _ => false }", "a && (o switch { 1 => true, _ => false })")]
    [InlineData("a", "o is int ? b : c", "a && (o is int ? b : c)")]
    public Task Parentheses_OnlyWhereNeeded(string outer, string inner, string merged) => FixAsync(
        $$"""
        class C
        {
            void M(bool a, bool b, bool c, object o)
            {
                {|NLR0008:if|} ({{outer}})
                {
                    if ({{inner}})
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """,
        $$"""
        class C
        {
            void M(bool a, bool b, bool c, object o)
            {
                if ({{merged}})
                {
                    Run();
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task Parentheses_AssignmentInTheCondition() => FixAsync(
        """
        class C
        {
            bool flag;

            void M(bool b, bool c)
            {
                {|NLR0008:if|} (flag = c)
                {
                    if (b || (flag = c))
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            bool flag;

            void M(bool b, bool c)
            {
                if ((flag = c) && (b || (flag = c)))
                {
                    Run();
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task Parentheses_ChainWithLooseOperators() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                {|NLR0008:if|} (a || b)
                    if (b || c)
                        if (c ? a : b)
                            Run();
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                if ((a || b) && (b || c) && (c ? a : b))
                    Run();
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task MultiLineCondition_IsKept() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                {|NLR0008:if|} (a
                    || c)
                {
                    if (b)
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                if ((a
                    || c) && b)
                {
                    Run();
                }
            }

            void Run() { }
        }
        """);

    // --- Variables declared in conditions -----------------------------------------------------------------

    [Fact]
    public Task PatternVariable_StaysInScopeForTheBody() => FixAsync(
        """
        class C
        {
            void M(object o)
            {
                {|NLR0008:if|} (o is string s)
                {
                    if (s.Length > 0)
                    {
                        Use(s);
                    }
                }
            }

            void Use(string s) { }
        }
        """,
        """
        class C
        {
            void M(object o)
            {
                if (o is string s && s.Length > 0)
                {
                    Use(s);
                }
            }

            void Use(string s) { }
        }
        """);

    [Fact]
    public Task OutVariables_InBothConditions() => FixAsync(
        """
        class C
        {
            void M(string a, string b)
            {
                {|NLR0008:if|} (int.TryParse(a, out var x))
                {
                    if (int.TryParse(b, out var y))
                    {
                        Use(x + y);
                    }
                }
            }

            void Use(int n) { }
        }
        """,
        """
        class C
        {
            void M(string a, string b)
            {
                if (int.TryParse(a, out var x) && int.TryParse(b, out var y))
                {
                    Use(x + y);
                }
            }

            void Use(int n) { }
        }
        """);

    [Fact]
    public Task InnerVariable_WithADifferentName_IsFine() => FixAsync(
        """
        class C
        {
            int u;

            void M(object o)
            {
                {|NLR0008:if|} (u == 1)
                {
                    if (o is int v)
                    {
                        Use(v);
                    }
                }
            }

            void Use(int n) { }
        }
        """,
        """
        class C
        {
            int u;

            void M(object o)
            {
                if (u == 1 && o is int v)
                {
                    Use(v);
                }
            }

            void Use(int n) { }
        }
        """);

    [Fact]
    public Task InnerVariable_NamedLikeAnIdentifierOfTheOuterCondition_IsNotReported() => NoDiagnosticAsync(
        """
        class C
        {
            int u;

            void M(object o)
            {
                if (u == 1)
                {
                    if (o is int u)
                    {
                        Use(u);
                    }
                }
            }

            void Use(int n) { }
        }
        """);

    [Fact]
    public Task ChainStopsWhereAVariableWouldClash() => FixAsync(
        """
        class C
        {
            int u;

            void M(object o, bool b)
            {
                {|NLR0008:if|} (u == 1)
                {
                    if (b)
                    {
                        if (o is int u)
                        {
                            Use(u);
                        }
                    }
                }
            }

            void Use(int n) { }
        }
        """,
        """
        class C
        {
            int u;

            void M(object o, bool b)
            {
                if (u == 1 && b)
                {
                    if (o is int u)
                    {
                        Use(u);
                    }
                }
            }

            void Use(int n) { }
        }
        """);

    // --- Fix all ----------------------------------------------------------------------------------------

    [Fact]
    public Task FixAll_SeveralStatementsInOneFile() => FixAsync(
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                {|NLR0008:if|} (a)
                {
                    if (b)
                    {
                        Run();
                    }
                }

                Run();

                {|NLR0008:if|} (b) if (c) Run();

                while (a)
                {
                    {|NLR0008:if|} (c || b)
                        if (a)
                        {
                            Run();
                        }
                }
            }

            void Run() { }
        }
        """,
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                if (a && b)
                {
                    Run();
                }

                Run();

                if (b && c) Run();

                while (a)
                {
                    if ((c || b) && a)
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """);

    // --- Not reported -----------------------------------------------------------------------------------

    [Fact]
    public Task NotReported_OuterElse() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
                    if (b)
                    {
                        Run();
                    }
                }
                else
                {
                    Run();
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task NotReported_InnerElse() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
                    if (b)
                    {
                        Run();
                    }
                    else
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task NotReported_InnerElseWithoutBraces_BindsToTheInnerIf() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                    if (b) Run();
                    else Run();
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task NotReported_ElseIfOfTheInnerIf() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b, bool c)
            {
                if (a)
                {
                    if (b)
                    {
                        Run();
                    }
                    else if (c)
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """);

    [Theory]
    [InlineData("Run(); if (b) { Run(); }")]
    [InlineData("if (b) { Run(); } Run();")]
    [InlineData("int i = 0; if (b) { Run(); }")]
    [InlineData("if (b) { Run(); } if (a) { Run(); }")]
    [InlineData("if (b) { Run(); } ;")]
    public Task NotReported_OuterHasAnotherStatement(string body) => NoDiagnosticAsync(
        $$"""
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
                    {{body}}
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task NotReported_InnerIsNotAnIf() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
                    while (b)
                    {
                        Run();
                    }
                }

                if (a)
                    for (;;) if (b) Run();
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task NotReported_NestedIfInsideASwitchOrLambdaOfTheOuterBody() => NoDiagnosticAsync(
        """
        using System;

        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
                    Action f = () => { if (b) Run(); };
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task NotReported_CommentBetweenTheTwoIfs() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
                    // only when b too
                    if (b)
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """);

    [Theory]
    [InlineData("if (a) // outer\n        {\n            if (b)\n            {\n                Run();\n            }\n        }")]
    [InlineData("if (a)\n        {\n            if (b)\n            {\n                Run();\n            } // inner end\n        }")]
    [InlineData("if (a /* x */)\n            if (b)\n                Run();")]
    [InlineData("if (a)\n            if (/* y */ b)\n                Run();")]
    public Task NotReported_CommentThatWouldBeLost(string statement) =>
        NoDiagnosticAsync($"class C\n{{\n    void M(bool a, bool b)\n    {{\n        {statement}\n    }}\n\n    void Run() {{ }}\n}}\n");

    [Fact]
    public Task NotReported_Directive() => NoDiagnosticAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                if (a)
                {
        #if DEBUG
                    if (b)
                    {
                        Run();
                    }
        #endif
                }
            }

            void Run() { }
        }
        """);

    [Fact]
    public Task NotReported_ConditionsThatAreNotBool() => NoDiagnosticAsync(
        """
        struct Flag
        {
            public static bool operator true(Flag f) => true;
            public static bool operator false(Flag f) => false;
            public static Flag operator &(Flag x, Flag y) => x;
            public static implicit operator Flag(bool b) => default;
        }

        class C
        {
            void M(Flag f, Flag g, dynamic d, bool b)
            {
                if (f)
                {
                    if (g)
                    {
                        Run();
                    }
                }

                if (d)
                {
                    if (b)
                    {
                        Run();
                    }
                }
            }

            void Run() { }
        }
        """);

    // --- Interaction with the guard clause rule (NLR0001) ---------------------------------------------------------

    [Fact]
    public Task Nlr0001_DefaultSettings_OnlyTheMergeIsSuggested() =>
        BothRules.RunAsync(
            """
            class C
            {
                void M(bool a, bool b)
                {
                    {|NLR0008:if|} (a)
                    {
                        if (b)
                        {
                            Run();
                            Run();
                            Run();
                        }
                    }
                }

                void Run() { }
            }
            """);

    [Fact]
    public Task Nlr0001_MinStatementsOne_BothSuggestionsAreOffered() =>
        BothRules.RunAsync(
            """
            class C
            {
                void M(bool a, bool b)
                {
                    {|NLR0001:{|NLR0008:if|}|} (a)
                    {
                        if (b)
                        {
                            Run();
                        }
                    }
                }

                void Run() { }
            }
            """,
            "nlr0001.min_statements = 1");

    [Fact]
    public Task Nlr0001_AfterTheMerge_OnlyTheGuardClauseRemains() =>
        BothRules.RunAsync(
            """
            class C
            {
                void M(bool a, bool b)
                {
                    {|NLR0001:if|} (a && b)
                    {
                        Run();
                    }
                }

                void Run() { }
            }
            """,
            "nlr0001.min_statements = 1");

    [Fact]
    public Task Nlr0001_AfterTheGuardClause_NothingToMerge() =>
        BothRules.RunAsync(
            """
            class C
            {
                void M(bool a, bool b)
                {
                    if (!a)
                    {
                        return;
                    }

                    {|NLR0001:if|} (b)
                    {
                        Run();
                    }
                }

                void Run() { }
            }
            """,
            "nlr0001.min_statements = 1");

    [Fact]
    public Task Nlr0001_AfterTheGuardClauseOfALoop_NothingToMerge() =>
        BothRules.RunAsync(
            """
            class C
            {
                void M(bool a, bool b)
                {
                    foreach (var i in new[] { 1, 2 })
                    {
                        if (!a)
                        {
                            continue;
                        }

                        {|NLR0001:if|} (b)
                        {
                            Run();
                        }
                    }
                }

                void Run() { }
            }
            """,
            "nlr0001.min_statements = 1");
}
