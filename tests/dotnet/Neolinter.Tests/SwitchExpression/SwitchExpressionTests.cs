using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.SwitchExpression;
using Neolinter.CodeFixes.SwitchExpression;

namespace Neolinter.Tests.SwitchExpression;

/// <summary>NLR0019 tests. Several diagnostics in one source also exercise fix all.</summary>
public class SwitchExpressionTests
{
    private static Task Run(string source, string fixedSource, LanguageVersion version)
    {
        var test = new CSharpCodeFixTest<SwitchExpressionAnalyzer, SwitchExpressionCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var options = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
            return solution.WithProjectParseOptions(projectId, options.WithLanguageVersion(version));
        });

        return test.RunAsync();
    }

    private static Task FixAsync(string source, string fixedSource, LanguageVersion version = LanguageVersion.Latest) => Run(source, fixedSource, version);

    private static Task NoDiagnosticAsync(string source, LanguageVersion version = LanguageVersion.Latest) => Run(source, source, version);

    private static string Class(string members) =>
        "using System;\n\nenum Color { Red, Green, Blue }\n\nclass C\n{\n    int _field;\n    int[] _array = new int[3];\n\n" + members + "}\n";

    // --- Reported ------------------------------------------------------------------

    [Fact]
    public Task ReturnWithDefault() => FixAsync(
        Class("    string M(int n)\n    {\n        {|NLR0019:switch|} (n)\n        {\n            case 1:\n                return \"one\";\n            case 2:\n                return \"two\";\n            default:\n                return \"many\";\n        }\n    }\n"),
        Class("    string M(int n)\n    {\n        return n switch\n        {\n            1 => \"one\",\n            2 => \"two\",\n            _ => \"many\",\n        };\n    }\n"));

    [Fact]
    public Task AssignmentToTheSameVariable() => FixAsync(
        Class("    void M(int n)\n    {\n        int result;\n        {|NLR0019:switch|} (n)\n        {\n            case 1:\n                result = 10;\n                break;\n            default:\n                result = 20;\n                break;\n        }\n\n        Console.WriteLine(result);\n    }\n"),
        Class("    void M(int n)\n    {\n        int result;\n        result = n switch\n        {\n            1 => 10,\n            _ => 20,\n        };\n\n        Console.WriteLine(result);\n    }\n"));

    [Fact]
    public Task AssignmentToAField() => FixAsync(
        Class("    void M(int n)\n    {\n        {|NLR0019:switch|} (n)\n        {\n            case 1: this._field = 10; break;\n            default: this._field = 20; break;\n        }\n    }\n"),
        Class("    void M(int n)\n    {\n        this._field = n switch\n        {\n            1 => 10,\n            _ => 20,\n        };\n    }\n"));

    [Fact]
    public Task StackedLabelsBecomeOr() => FixAsync(
        Class("    bool M(char c)\n    {\n        {|NLR0019:switch|} (c)\n        {\n            case 'a':\n            case 'e':\n            case 'i':\n                return true;\n            default:\n                return false;\n        }\n    }\n"),
        Class("    bool M(char c)\n    {\n        return c switch\n        {\n            'a' or 'e' or 'i' => true,\n            _ => false,\n        };\n    }\n"));

    [Fact]
    public Task PatternsAndGuards() => FixAsync(
        Class("    string M(object o)\n    {\n        {|NLR0019:switch|} (o)\n        {\n            case null:\n                return \"null\";\n            case int n when n > 5:\n                return \"big \" + n;\n            case string s:\n                return s;\n            case long:\n                return \"x\";\n            default:\n                return \"other\";\n        }\n    }\n"),
        Class("    string M(object o)\n    {\n        return o switch\n        {\n            null => \"null\",\n            int n when n > 5 => \"big \" + n,\n            string s => s,\n            long => \"x\",\n            _ => \"other\",\n        };\n    }\n"));

    [Fact]
    public Task ThrowingDefault() => FixAsync(
        Class("    int M(Color color)\n    {\n        {|NLR0019:switch|} (color)\n        {\n            case Color.Red: return 1;\n            case Color.Green: return 2;\n            default: throw new ArgumentOutOfRangeException(nameof(color));\n        }\n    }\n"),
        Class("    int M(Color color)\n    {\n        return color switch\n        {\n            Color.Red => 1,\n            Color.Green => 2,\n            _ => throw new ArgumentOutOfRangeException(nameof(color)),\n        };\n    }\n"));

    [Fact]
    public Task BoolSwitchWithoutDefault() => FixAsync(
        Class("    int M(bool b)\n    {\n        {|NLR0019:switch|} (b)\n        {\n            case true: return 1;\n            case false: return 0;\n        }\n    }\n"),
        Class("    int M(bool b)\n    {\n        return b switch\n        {\n            true => 1,\n            false => 0,\n        };\n    }\n"));

    [Fact]
    public Task BlockSectionsAndComplexGoverningExpression() => FixAsync(
        Class("    int M(int a, int b)\n    {\n        {|NLR0019:switch|} (a + b)\n        {\n            case 1: { return 10; }\n            default: { return 20; }\n        }\n    }\n"),
        Class("    int M(int a, int b)\n    {\n        return (a + b) switch\n        {\n            1 => 10,\n            _ => 20,\n        };\n    }\n"));

    [Fact]
    public Task IndentationAndTriviaAreKept() => FixAsync(
        "class C\n{\n\tint M(int n)\n\t{\n\t\tvar x = 1;\n\n\t\t// leading comment\n\t\t{|NLR0019:switch|} (n)\n\t\t{\n\t\t\tcase 1:\n\t\t\t\treturn 10;\n\t\t\tdefault:\n\t\t\t\treturn x;\n\t\t}\n\t}\n}\n",
        "class C\n{\n\tint M(int n)\n\t{\n\t\tvar x = 1;\n\n\t\t// leading comment\n\t\treturn n switch\n\t\t{\n\t\t\t1 => 10,\n\t\t\t_ => x,\n\t\t};\n\t}\n}\n");

    [Fact]
    public Task CSharp8WithSingleLabels() => FixAsync(
        Class("    string M(int n)\n    {\n        {|NLR0019:switch|} (n)\n        {\n            case 1: return \"one\";\n            default: return \"many\";\n        }\n    }\n"),
        Class("    string M(int n)\n    {\n        return n switch\n        {\n            1 => \"one\",\n            _ => \"many\",\n        };\n    }\n"),
        LanguageVersion.CSharp8);

    [Fact]
    public Task TargetTypedArmsInCSharp9() => FixAsync(
        Class("    double? M(int n)\n    {\n        {|NLR0019:switch|} (n)\n        {\n            case 1: return 1.5;\n            default: return null;\n        }\n    }\n"),
        Class("    double? M(int n)\n    {\n        return n switch\n        {\n            1 => 1.5,\n            _ => null,\n        };\n    }\n"));

    [Fact]
    public Task FixAll() => FixAsync(
        Class("    int A(int n)\n    {\n        {|NLR0019:switch|} (n)\n        {\n            case 1: return 1;\n            default: return 2;\n        }\n    }\n\n    int B(int n)\n    {\n        {|NLR0019:switch|} (n)\n        {\n            case 3: return 3;\n            default: return 4;\n        }\n    }\n"),
        Class("    int A(int n)\n    {\n        return n switch\n        {\n            1 => 1,\n            _ => 2,\n        };\n    }\n\n    int B(int n)\n    {\n        return n switch\n        {\n            3 => 3,\n            _ => 4,\n        };\n    }\n"));

    // --- Not reported --------------------------------------------------------------

    [Fact]
    public Task NotReportedWithoutDefault() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        switch (n)\n        {\n            case 1: return 1;\n            case 2: return 2;\n        }\n\n        return 0;\n    }\n"));

    [Fact]
    public Task NotReportedEnumCoveredButNoDefault() => NoDiagnosticAsync(
        Class("    int M(Color c)\n    {\n        switch (c)\n        {\n            case Color.Red: return 1;\n            case Color.Green: return 2;\n            case Color.Blue: return 3;\n        }\n\n        return 0;\n    }\n"));

    [Fact]
    public Task NotReportedDefaultNotLast() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        switch (n)\n        {\n            default: return 0;\n            case 1: return 1;\n        }\n    }\n"));

    [Fact]
    public Task NotReportedSeveralStatements() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        switch (n)\n        {\n            case 1:\n                Console.WriteLine();\n                return 1;\n            default: return 0;\n        }\n    }\n"));

    [Fact]
    public Task NotReportedFallthroughAndGoto() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        int x = 0;\n        switch (n)\n        {\n            case 1: x = 1; goto case 2;\n            case 2: x = 2; break;\n            default: x = 3; break;\n        }\n\n        return x;\n    }\n"));

    [Fact]
    public Task NotReportedComment() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        switch (n)\n        {\n            case 1: return 1; // one\n            default: return 0;\n        }\n    }\n"));

    [Fact]
    public Task NotReportedDifferentTargets() => NoDiagnosticAsync(
        Class("    void M(int n)\n    {\n        int a = 0, b = 0;\n        switch (n)\n        {\n            case 1: a = 1; break;\n            default: b = 2; break;\n        }\n    }\n"));

    [Fact]
    public Task NotReportedMixedReturnAndAssignment() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        int a;\n        switch (n)\n        {\n            case 1: a = 1; break;\n            default: return 2;\n        }\n\n        return a;\n    }\n"));

    [Fact]
    public Task NotReportedTargetWithSideEffects() => NoDiagnosticAsync(
        Class("    void M(int n)\n    {\n        switch (n)\n        {\n            case 1: _array[n++] = 1; break;\n            default: _array[n++] = 2; break;\n        }\n    }\n"));

    [Fact]
    public Task NotReportedCompoundAssignment() => NoDiagnosticAsync(
        Class("    void M(int n)\n    {\n        switch (n)\n        {\n            case 1: _field += 1; break;\n            default: _field += 2; break;\n        }\n    }\n"));

    [Fact]
    public Task NotReportedVoidReturn() => NoDiagnosticAsync(
        Class("    void M(int n)\n    {\n        switch (n)\n        {\n            case 1: return;\n            default: return;\n        }\n    }\n"));

    [Fact]
    public Task NotReportedOnlyThrows() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        switch (n)\n        {\n            case 1: throw new Exception();\n            default: throw new ArgumentException(\"x\");\n        }\n    }\n"));

    [Fact]
    public Task NotReportedWhenTheNaturalTypeDiffersFromTheTarget() => NoDiagnosticAsync(
        Class("    object M(int n)\n    {\n        switch (n)\n        {\n            case 1: return 1;\n            default: return 2.5;\n        }\n    }\n"));

    [Fact]
    public Task NotReportedStackedLabelsInCSharp8() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        switch (n)\n        {\n            case 1:\n            case 2: return 1;\n            default: return 0;\n        }\n    }\n"),
        LanguageVersion.CSharp8);

    [Fact]
    public Task NotReportedTargetTypingInCSharp8() => NoDiagnosticAsync(
        Class("    double? M(int n)\n    {\n        switch (n)\n        {\n            case 1: return 1.5;\n            default: return null;\n        }\n    }\n"),
        LanguageVersion.CSharp8);

    [Fact]
    public Task NotReportedBeforeCSharp8() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        switch (n)\n        {\n            case 1: return 1;\n            default: return 0;\n        }\n    }\n"),
        LanguageVersion.CSharp7_3);

    [Fact]
    public Task NotReportedGuardedStackedLabels() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        switch (n)\n        {\n            case 1:\n            case int x when x > 5: return 1;\n            default: return 0;\n        }\n    }\n"));

    [Fact]
    public Task NotReportedMultiLineValue() => NoDiagnosticAsync(
        Class("    int M(int n)\n    {\n        switch (n)\n        {\n            case 1:\n                return Math.Max(\n                    1, n);\n            default: return 0;\n        }\n    }\n"));
}
