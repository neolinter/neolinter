using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.NestedTernary;
using Neolinter.CodeFixes.NestedTernary;

namespace Neolinter.Tests.NestedTernary;

/// <summary>NLR0007 tests. Several diagnostics in one source also exercise fix all.</summary>
public class NestedTernaryTests
{
    private static Task FixAsync(string source, string fixedSource, string? editorConfig = null) => Run(source, fixedSource, editorConfig);

    /// <summary>The diagnostic is expected (mark it in <paramref name="source"/>) but there is no fix: the code stays as it is.</summary>
    private static Task NoFixAsync(string source) => Run(source, source, null);

    private static Task NoDiagnosticAsync(string source) => Run(source, source, null);

    private static Task Run(string source, string fixedSource, string? editorConfig)
    {
        var test = new CSharpCodeFixTest<NestedTernaryAnalyzer, NestedTernaryCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        if (editorConfig is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n\n[*]\n" + editorConfig + "\n"));
        }

        return test.RunAsync();
    }

    // --- Reported shapes (no fix needed here: the expression is an argument) ------------------

    [Theory]
    [InlineData("a ? 1 : b ? 2 : 3")]
    [InlineData("a ? b ? 1 : 2 : 3")]
    [InlineData("(a ? b : c) ? 1 : 2")]
    [InlineData("a ? 1 : (b ? 2 : 3)")]
    [InlineData("a ? 1 : b ? 2 : c ? 3 : 4")]
    public Task Reported_AnyPositionOfTheNestedConditional(string expression) => NoFixAsync(
        $$"""
        class C
        {
            void M(bool a, bool b, bool c) => Use({|NLR0007:{{expression}}|});

            void Use(int x) { }
        }
        """);

    [Fact]
    public Task Reported_OncePerOutermostExpression() => NoFixAsync(
        """
        class C
        {
            void M(bool a, bool b, bool c, bool d) => Use({|NLR0007:a ? (b ? 1 : 2) : (c ? 3 : d ? 4 : 5)|});

            void Use(int x) { }
        }
        """);

    [Fact]
    public Task Reported_ConditionalInsideALambdaIsJudgedOnItsOwn() => NoFixAsync(
        """
        using System;

        class C
        {
            Func<int> M(bool a, bool b, bool c) => a ? () => {|NLR0007:b ? 1 : c ? 2 : 3|} : () => 0;
        }
        """);

    // --- Not reported ---------------------------------------------------------------------------

    [Fact]
    public Task NotReported_SingleConditional() => NoDiagnosticAsync(
        """
        class C
        {
            int M(bool a) => a ? 1 : 2;
        }
        """);

    [Fact]
    public Task NotReported_TwoSeparateConditionals() => NoDiagnosticAsync(
        """
        class C
        {
            int M(bool a, bool b) => (a ? 1 : 2) + (b ? 3 : 4);
        }
        """);

    [Fact]
    public Task NotReported_LambdaInsideAConditional() => NoDiagnosticAsync(
        """
        using System;

        class C
        {
            Func<int> M(bool a, bool b) => a ? () => b ? 1 : 2 : null;
        }
        """);

    // --- Code fix ---------------------------------------------------------------------------------

    [Fact]
    public Task Return_FlatChain() => FixAsync(
        """
        class C
        {
            string M(int n)
            {
                return {|NLR0007:n < 0 ? "negative" : n == 0 ? "zero" : "positive"|};
            }
        }
        """,
        """
        class C
        {
            string M(int n)
            {
                if (n < 0)
                {
                    return "negative";
                }
                else if (n == 0)
                {
                    return "zero";
                }
                else
                {
                    return "positive";
                }
            }
        }
        """);

    [Fact]
    public Task Return_ParenthesizedFalseBranchIsPartOfTheChain() => FixAsync(
        """
        class C
        {
            int M(bool a, bool b)
            {
                return {|NLR0007:a ? 1 : (b ? 2 : 3)|};
            }
        }
        """,
        """
        class C
        {
            int M(bool a, bool b)
            {
                if (a)
                {
                    return 1;
                }
                else if (b)
                {
                    return 2;
                }
                else
                {
                    return 3;
                }
            }
        }
        """);

    [Fact]
    public Task Assignment_ToLocalFieldAndThisMember() => FixAsync(
        """
        class C
        {
            int f;

            void M(int n)
            {
                int x;
                x = {|NLR0007:n > 0 ? 1 : n < 0 ? -1 : 0|};
                f = {|NLR0007:n > 5 ? 1 : n > 2 ? 2 : 3|};
                this.f = {|NLR0007:n > 7 ? 1 : n > 3 ? 2 : 3|};
            }
        }
        """,
        """
        class C
        {
            int f;

            void M(int n)
            {
                int x;
                if (n > 0)
                {
                    x = 1;
                }
                else if (n < 0)
                {
                    x = -1;
                }
                else
                {
                    x = 0;
                }
                if (n > 5)
                {
                    f = 1;
                }
                else if (n > 2)
                {
                    f = 2;
                }
                else
                {
                    f = 3;
                }
                if (n > 7)
                {
                    this.f = 1;
                }
                else if (n > 3)
                {
                    this.f = 2;
                }
                else
                {
                    this.f = 3;
                }
            }
        }
        """);

    [Fact]
    public Task Declaration_ExplicitTypeIsSplit() => FixAsync(
        """
        class C
        {
            void M(int n)
            {
                string label = {|NLR0007:n < 0 ? "neg" : n == 0 ? "zero" : "pos"|};
                System.Console.WriteLine(label);
            }
        }
        """,
        """
        class C
        {
            void M(int n)
            {
                string label;
                if (n < 0)
                {
                    label = "neg";
                }
                else if (n == 0)
                {
                    label = "zero";
                }
                else
                {
                    label = "pos";
                }
                System.Console.WriteLine(label);
            }
        }
        """);

    [Fact]
    public Task SwitchSection_Return() => FixAsync(
        """
        class C
        {
            int M(int kind, bool a, bool b)
            {
                switch (kind)
                {
                    case 1:
                        return {|NLR0007:a ? 1 : b ? 2 : 3|};
                    default:
                        return 0;
                }
            }
        }
        """,
        """
        class C
        {
            int M(int kind, bool a, bool b)
            {
                switch (kind)
                {
                    case 1:
                        if (a)
                        {
                            return 1;
                        }
                        else if (b)
                        {
                            return 2;
                        }
                        else
                        {
                            return 3;
                        }
                    default:
                        return 0;
                }
            }
        }
        """);

    [Fact]
    public Task CommentsAroundTheStatementAndInsideOperandsAreKept() => FixAsync(
        """
        class C
        {
            int M(int n)
            {
                // before

                return {|NLR0007:n < 0 ? Abs(/* inner */ n) : n == 0 ? 0 : n|}; // after
            }

            int Abs(int x) => x;
        }
        """,
        """
        class C
        {
            int M(int n)
            {
                // before

                if (n < 0)
                {
                    return Abs(/* inner */ n);
                }
                else if (n == 0)
                {
                    return 0;
                }
                else
                {
                    return n;
                } // after
            }

            int Abs(int x) => x;
        }
        """);

    [Fact]
    public Task EvaluationOrderIsThatOfTheConditionalExpression() => FixAsync(
        """
        class C
        {
            int M()
            {
                return {|NLR0007:First() ? Value(1) : Second() ? Value(2) : Value(3)|};
            }

            bool First() => true;
            bool Second() => true;
            int Value(int v) => v;
        }
        """,
        """
        class C
        {
            int M()
            {
                if (First())
                {
                    return Value(1);
                }
                else if (Second())
                {
                    return Value(2);
                }
                else
                {
                    return Value(3);
                }
            }

            bool First() => true;
            bool Second() => true;
            int Value(int v) => v;
        }
        """);

    [Fact]
    public Task Return_MayDeclarePatternVariables() => FixAsync(
        """
        class C
        {
            string M(object o)
            {
                return {|NLR0007:o is string s ? s : o is int i ? i.ToString() : "?"|};
            }
        }
        """,
        """
        class C
        {
            string M(object o)
            {
                if (o is string s)
                {
                    return s;
                }
                else if (o is int i)
                {
                    return i.ToString();
                }
                else
                {
                    return "?";
                }
            }
        }
        """);

    [Fact]
    public Task SameTypeAsTheWholeExpression_NullableAndBaseTypesAreFine() => FixAsync(
        """
        class C
        {
            int? M(bool a, bool b, int? x, int? y)
            {
                return {|NLR0007:a ? x : b ? y : x|};
            }
        }
        """,
        """
        class C
        {
            int? M(bool a, bool b, int? x, int? y)
            {
                if (a)
                {
                    return x;
                }
                else if (b)
                {
                    return y;
                }
                else
                {
                    return x;
                }
            }
        }
        """);

    // --- Layout and braces ------------------------------------------------------------------------

    [Fact]
    public Task Layout_TabsAndBraceOnTheSameLineAreFollowed() => FixAsync(
        "class C {\n\tint M(bool a, bool b) {\n\t\treturn {|NLR0007:a ? 1 : b ? 2 : 3|};\n\t}\n}\n",
        "class C {\n\tint M(bool a, bool b) {\n\t\tif (a) {\n\t\t\treturn 1;\n\t\t} else if (b) {\n\t\t\treturn 2;\n\t\t} else {\n\t\t\treturn 3;\n\t\t}\n\t}\n}\n");

    [Fact]
    public Task Layout_WindowsLineEndingsAreKept() => FixAsync(
        "class C\r\n{\r\n    int M(bool a, bool b)\r\n    {\r\n        return {|NLR0007:a ? 1 : b ? 2 : 3|};\r\n    }\r\n}\r\n",
        "class C\r\n{\r\n    int M(bool a, bool b)\r\n    {\r\n        if (a)\r\n        {\r\n            return 1;\r\n        }\r\n        else if (b)\r\n        {\r\n            return 2;\r\n        }\r\n        else\r\n        {\r\n            return 3;\r\n        }\r\n    }\r\n}\r\n");

    [Fact]
    public Task Braces_PreferBracesFalseWritesNone() => FixAsync(
        """
        class C
        {
            int M(bool a, bool b)
            {
                return {|NLR0007:a ? 1 : b ? 2 : 3|};
            }
        }
        """,
        """
        class C
        {
            int M(bool a, bool b)
            {
                if (a)
                    return 1;
                else if (b)
                    return 2;
                else
                    return 3;
            }
        }
        """,
        "csharp_prefer_braces = false:suggestion");

    [Fact]
    public Task Braces_WhenMultilineOnlyForMultilineBranches() => FixAsync(
        """
        class C
        {
            int M(bool a, bool b)
            {
                return {|NLR0007:a ? 1 : b ? Sum(1,
                    2) : 3|};
            }

            int Sum(int x, int y) => x + y;
        }
        """,
        """
        class C
        {
            int M(bool a, bool b)
            {
                if (a)
                {
                    return 1;
                }
                else if (b)
                {
                    return Sum(1,
                    2);
                }
                else
                {
                    return 3;
                }
            }

            int Sum(int x, int y) => x + y;
        }
        """,
        "csharp_prefer_braces = when_multiline");

    [Fact]
    public Task Braces_FileWithoutBracesInItsIfStatementsGetsNone() => FixAsync(
        """
        class C
        {
            int M(bool a, bool b)
            {
                return {|NLR0007:a ? 1 : b ? 2 : 3|};
            }

            int Other(bool a)
            {
                if (a)
                    return 1;
                else
                    return 2;
            }
        }
        """,
        """
        class C
        {
            int M(bool a, bool b)
            {
                if (a)
                    return 1;
                else if (b)
                    return 2;
                else
                    return 3;
            }

            int Other(bool a)
            {
                if (a)
                    return 1;
                else
                    return 2;
            }
        }
        """);

    // --- Fix all ----------------------------------------------------------------------------------

    [Fact]
    public Task FixAll_SeveralStatements() => FixAsync(
        """
        class C
        {
            int M(bool a, bool b)
            {
                int x = {|NLR0007:a ? 1 : b ? 2 : 3|};
                if (x > 1)
                {
                    return {|NLR0007:b ? 4 : a ? 5 : 6|};
                }

                return x;
            }
        }
        """,
        """
        class C
        {
            int M(bool a, bool b)
            {
                int x;
                if (a)
                {
                    x = 1;
                }
                else if (b)
                {
                    x = 2;
                }
                else
                {
                    x = 3;
                }
                if (x > 1)
                {
                    if (b)
                    {
                        return 4;
                    }
                    else if (a)
                    {
                        return 5;
                    }
                    else
                    {
                        return 6;
                    }
                }

                return x;
            }
        }
        """);

    // --- No fix: contexts ------------------------------------------------------------------------

    [Fact]
    public Task NoFix_Contexts() => NoFixAsync(
        """
        using System;

        class C
        {
            int P(bool a, bool b) => {|NLR0007:a ? 1 : b ? 2 : 3|};

            void M(bool a, bool b, int[] items, Func<bool, int> f)
            {
                Use({|NLR0007:a ? 1 : b ? 2 : 3|});
                var s = $"{({|NLR0007:a ? 1 : b ? 2 : 3|})}";
                Func<int> g = () => {|NLR0007:a ? 1 : b ? 2 : 3|};
                var t = 1 + ({|NLR0007:a ? 1 : b ? 2 : 3|});
                items[0] = {|NLR0007:a ? 1 : b ? 2 : 3|};
                if (a) Use2({|NLR0007:a ? 1 : b ? 2 : 3|});
            }

            void N(bool a, bool b)
            {
                if (a)
                    return;

                foreach (var i in new[] { 1 })
                    Use({|NLR0007:a ? 1 : b ? 2 : 3|});
            }

            int Q(bool a, bool b)
            {
                if (a)
                    return {|NLR0007:a ? 1 : b ? 2 : 3|};

                return 0;
            }

            void Use(int x) { }
            void Use2(int x) { }
        }
        """);

    [Fact]
    public Task NoFix_AssignmentTargetsThatAreEvaluatedBeforeTheConditions() => NoFixAsync(
        """
        class C
        {
            int[] items = new int[4];
            int i;

            C Next() => this;

            int F;

            void M(bool a, bool b)
            {
                items[i++] = {|NLR0007:a ? 1 : b ? 2 : 3|};
                Next().F = {|NLR0007:a ? 1 : b ? 2 : 3|};
                F += {|NLR0007:a ? 1 : b ? 2 : 3|};
            }
        }
        """);

    [Fact]
    public Task NoFix_DeclarationsThatCannotBeSplit() => NoFixAsync(
        """
        class C
        {
            void M(bool a, bool b)
            {
                var v = {|NLR0007:a ? 1 : b ? 2 : 3|};
                const int k = 1;
                int p = {|NLR0007:a ? 1 : b ? 2 : 3|}, q = 0;
            }
        }
        """);

    [Fact]
    public Task NoFix_NestingOutsideTheFalseBranches() => NoFixAsync(
        """
        class C
        {
            int M(bool a, bool b, bool c)
            {
                var x = 0;
                x = {|NLR0007:a ? b ? 1 : 2 : 3|};
                x = {|NLR0007:(a ? b : c) ? 1 : 2|};
                return {|NLR0007:a ? b ? 1 : 2 : 3|};
            }
        }
        """);

    [Fact]
    public Task NoFix_PatternVariableWouldLoseItsScope() => NoFixAsync(
        """
        class C
        {
            void M(object o)
            {
                string r;
                r = {|NLR0007:o is string s ? s : o is int i ? i.ToString() : "?"|};
                string r2 = {|NLR0007:o is string s2 ? s2 : o is int i2 ? i2.ToString() : "?"|};
            }
        }
        """);

    [Fact]
    public Task NoFix_CommentNextToTheOperators() => NoFixAsync(
        """
        class C
        {
            int M(bool a, bool b)
            {
                return {|NLR0007:a ? 1 // one
                    : b ? 2 // two
                    : 3|};
            }
        }
        """);

    [Fact]
    public Task NoFix_Directive() => NoFixAsync(
        """
        class C
        {
            int M(bool a, bool b)
            {
                return {|NLR0007:a ? 1
        #if true
                    : b ? 2
        #endif
                    : 3|};
            }
        }
        """);

    [Fact]
    public Task NoFix_StatementDoesNotStartItsLine() => NoFixAsync(
        """
        class C
        {
            int M(bool a, bool b) { return {|NLR0007:a ? 1 : b ? 2 : 3|}; }
        }
        """);

    // --- No fix: typing -------------------------------------------------------------------------

    [Fact]
    public Task NoFix_TargetTypedConditional() => NoFixAsync(
        """
        class C
        {
            int? M(bool a, bool b)
            {
                int? x;
                x = {|NLR0007:a ? 1 : b ? 2 : null|};
                return {|NLR0007:a ? 1 : b ? 2 : null|};
            }
        }
        """);

    [Fact]
    public Task NoFix_MixedBranchTypes() => NoFixAsync(
        """
        class C
        {
            object M(bool a, bool b)
            {
                return {|NLR0007:a ? 1 : b ? 2.5 : 3|};
            }

            object N(bool a, bool b)
            {
                object o = {|NLR0007:a ? 1 : b ? 2.5 : 3|};
                return o;
            }
        }
        """);

    [Fact]
    public Task NoFix_BranchesOfRelatedTypesThatOnlyConvertWithATarget() => NoFixAsync(
        """
        class Base { }
        class A : Base { }
        class B : Base { }

        class C
        {
            Base M(bool a, bool b, A x, B y)
            {
                return {|NLR0007:a ? x : b ? y : x|};
            }
        }
        """);

    [Fact]
    public Task NoFix_LambdaBranches() => NoFixAsync(
        """
        using System;

        class C
        {
            Func<int> M(bool a, bool b)
            {
                return {|NLR0007:a ? () => 1 : b ? () => 2 : () => 3|};
            }
        }
        """);
}
