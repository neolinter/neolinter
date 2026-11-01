using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Neolinter.Cli.Core;

namespace Neolinter.Cli.Tests;

public class DuplicatesTests
{
    // Twelve statements with different shapes, so that no two of them hash alike. `x` is the variable prefix, `n` a literal.
    private static readonly string[] Templates =
    [
        "var {x}0 = Compute({x}, {n});",
        "if ({x}0 > {n}) { Log({x}0); }",
        "{x}1 = {x}0 * 2 + {n};",
        "for (var i = 0; i < {n}; i++) { total += i; }",
        "Log($\"value {{x}1}\");",
        "var {x}2 = new List<int> { {n}, 2, 3 };",
        "foreach (var item in {x}2) { total += item; }",
        "while (total > {n}) total--;",
        "{x}3 = {x}1 > {x}0 ? {x}1 : {x}0;",
        "Log({x}3.ToString(), \"label {n}\");",
        "total += Compute({x}3, {x}0);",
        "return total;",
    ];

    private static readonly DuplicatesConfig Defaults = new();

    /// <summary>Statements 0..count-1 of the template list, with the variable prefix and literal given.</summary>
    private static string Statements(string prefix, int number, int count = 8) =>
        string.Join("\n        ", Templates.Take(count).Select(t => t.Replace("{x}", prefix, StringComparison.Ordinal).Replace("{n}", number.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)));

    private static string Class(string name, string body) => $$"""
        public class {{name}}
        {
            private int total;

            public int Run(int a)
            {
                {{body}}
            }
        }
        """;

    private static IReadOnlyList<Finding> Analyze(DuplicatesConfig config, params (string Path, string Content)[] files)
    {
        using var directory = new TestPaths.TempDirectory();
        var trees = files.Select(f => CSharpSyntaxTree.ParseText(f.Content, path: Path.Combine(directory.Path, f.Path))).ToList();
        return DuplicatesAnalyzer.Analyze(trees, config, directory.Path);
    }

    private static string Relative(Finding finding) => Path.GetFileName(finding.Location.FilePath);

    [Fact]
    public void ReportsExactClonesInTwoFiles()
    {
        var findings = Analyze(Defaults, ("A.cs", Class("A", Statements("a", 5))), ("B.cs", Class("B", Statements("a", 5))));

        var finding = Assert.Single(findings);
        Assert.Equal("NLC0001", finding.RuleId);
        Assert.Equal("A.cs", Relative(finding));
        Assert.Equal("B.cs", Path.GetFileName(Assert.Single(finding.RelatedLocations).FilePath));
        Assert.Contains("8 statements", finding.Message);
    }

    [Fact]
    public void ReportsClonesWithRenamedIdentifiersAndOtherLiterals()
    {
        var findings = Analyze(Defaults, ("A.cs", Class("A", Statements("first", 5))), ("B.cs", Class("B", Statements("second", 99))));

        Assert.Single(findings);
    }

    [Fact]
    public void ReportsClonesInsideOneFile()
    {
        var findings = Analyze(Defaults, ("A.cs", Class("A", Statements("a", 1)) + "\n" + Class("B", Statements("b", 2))));

        var finding = Assert.Single(findings);
        Assert.Single(finding.RelatedLocations);
    }

    [Fact]
    public void DifferentStatementOrderIsNotAClone()
    {
        var reversed = string.Join("\n", Statements("a", 5).Split('\n').Reverse());

        Assert.Empty(Analyze(Defaults, ("A.cs", Class("A", Statements("a", 5))), ("B.cs", Class("B", reversed))));
    }

    [Fact]
    public void IgnoresSequencesBelowTheStatementThreshold()
    {
        // 7 identical statements, then one that differs: no window of 8 repeats.
        Assert.Empty(Analyze(
            Defaults,
            ("A.cs", Class("A", Statements("a", 5, 7) + "\n        Unlike(1, 2);")),
            ("B.cs", Class("B", Statements("a", 5, 7) + "\n        Other();"))));
    }

    [Fact]
    public void ThresholdFollowsConfiguration()
    {
        var files = new[] { ("A.cs", Class("A", Statements("a", 5, 5))), ("B.cs", Class("B", Statements("a", 5, 5))) };

        Assert.Empty(Analyze(Defaults, files));
        Assert.Single(Analyze(new DuplicatesConfig { MinStatements = 5, MinTokens = 0 }, files));
    }

    [Fact]
    public void IgnoresSequencesBelowTheTokenThreshold()
    {
        var trivial = string.Join("\n        ", Enumerable.Range(0, 8).Select(i => $"M{i}();"));
        var files = new[] { ("A.cs", Class("A", trivial)), ("B.cs", Class("B", trivial)) };

        Assert.Empty(Analyze(Defaults, files));
        Assert.Single(Analyze(new DuplicatesConfig { MinTokens = 0 }, files));
    }

    [Fact]
    public void OverlappingWindowsAreMergedIntoOneMaximalRange()
    {
        // 12 statements shared: five windows of 8 overlap, but there is a single group covering all 12.
        var findings = Analyze(Defaults, ("A.cs", Class("A", Statements("a", 5, 12))), ("B.cs", Class("B", Statements("b", 6, 12))));

        var finding = Assert.Single(findings);
        Assert.Contains("12 statements", finding.Message);
    }

    [Fact]
    public void GroupsEveryCopyUnderOneResult()
    {
        var findings = Analyze(
            Defaults,
            ("A.cs", Class("A", Statements("a", 1))),
            ("B.cs", Class("B", Statements("b", 2))),
            ("C.cs", Class("C", Statements("c", 3))));

        var finding = Assert.Single(findings);
        Assert.Equal(2, finding.RelatedLocations.Count);
        Assert.Contains("2 other places", finding.Message);
    }

    [Fact]
    public void ACloneInsideALargerCloneIsNotReportedAgain()
    {
        // The 8 statements of the `if` block repeat too, but only as part of the repeated method body.
        static string Body(string prefix, int number) =>
            Statements(prefix, number, 7) + "\n        if (a > 0)\n        {\n        " + Statements(prefix, number) + "\n        }";

        var findings = Analyze(Defaults, ("A.cs", Class("A", Body("a", 1))), ("B.cs", Class("B", Body("b", 2))));

        var finding = Assert.Single(findings);
        Assert.Contains("8 statements", finding.Message);
    }

    [Fact]
    public void RepeatedStatementsInOneBlockDoNotOverlapEachOther()
    {
        var statement = "Compute(a, 1, 2, 3, 4, 5);";
        var findings = Analyze(Defaults, ("A.cs", Class("A", string.Join("\n        ", Enumerable.Repeat(statement, 20)))));

        var finding = Assert.Single(findings);
        Assert.Contains("8 statements", finding.Message);
        Assert.Single(finding.RelatedLocations);
    }

    [Theory]
    [InlineData("Model.g.cs")]
    [InlineData("Form.Designer.cs")]
    public void GeneratedFileNamesAreIgnored(string generated)
    {
        var files = new[] { ("A.cs", Class("A", Statements("a", 1))), (generated, Class("B", Statements("b", 2))) };

        Assert.Empty(Analyze(Defaults, files));
        Assert.Single(Analyze(new DuplicatesConfig { ExcludeGenerated = false }, files));
    }

    [Fact]
    public void AutoGeneratedHeaderIsIgnored()
    {
        var generated = "// <auto-generated>\n// by a tool\n// </auto-generated>\n" + Class("B", Statements("b", 2));

        Assert.Empty(Analyze(Defaults, ("A.cs", Class("A", Statements("a", 1))), ("B.cs", generated)));
    }

    [Fact]
    public void MembersMarkedGeneratedCodeAreIgnored()
    {
        var generated = Class("B", Statements("b", 2)).Replace("public int Run", "[System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1\")]\n    public int Run", StringComparison.Ordinal);

        var files = new[] { ("A.cs", Class("A", Statements("a", 1))), ("B.cs", generated) };
        Assert.Empty(Analyze(Defaults, files));
        Assert.Single(Analyze(new DuplicatesConfig { ExcludeGenerated = false }, files));
    }

    [Fact]
    public void ExcludedPathsAreIgnored()
    {
        var files = new[] { ("src/A.cs", Class("A", Statements("a", 1))), ("legacy/B.cs", Class("B", Statements("b", 2))) };

        Assert.Single(Analyze(Defaults, files));
        Assert.Empty(Analyze(new DuplicatesConfig { ExcludePaths = ["legacy/**"] }, files));
    }

    [Fact]
    public void FilesOutsideTheSolutionDirectoryAreIgnored()
    {
        using var directory = new TestPaths.TempDirectory();
        using var other = new TestPaths.TempDirectory();
        var trees = new[]
        {
            CSharpSyntaxTree.ParseText(Class("A", Statements("a", 1)), path: Path.Combine(directory.Path, "A.cs")),
            CSharpSyntaxTree.ParseText(Class("B", Statements("b", 2)), path: Path.Combine(other.Path, "B.cs")),
        };

        Assert.Empty(DuplicatesAnalyzer.Analyze(trees, Defaults, directory.Path));
    }

    [Fact]
    public void OutputOrderDoesNotDependOnInputOrder()
    {
        var files = new[]
        {
            ("C.cs", Class("C", Statements("c", 3) + "\n        Extra(1, 2, 3, 4, 5, 6, 7, 8, 9);")),
            ("A.cs", Class("A", Statements("a", 1))),
            ("B.cs", Class("B", Statements("b", 2))),
            ("D.cs", Class("D", "Other(1, 2, 3);\n        " + Reversed("d", 4))),
            ("E.cs", Class("E", "Different(1);\n        " + Reversed("e", 5))),
        };

        var first = Describe(Analyze(Defaults, files));
        var second = Describe(Analyze(Defaults, files.Reverse().ToArray()));

        Assert.Equal(2, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(first.Order(StringComparer.Ordinal).ToList(), first);
    }

    // Reversed so that these two never match the forward sequences of the other files.
    private static string Reversed(string prefix, int number) =>
        string.Join("\n        ", Statements(prefix, number, 10).Split('\n').Select(l => l.Trim()).Reverse());

    private static List<string> Describe(IReadOnlyList<Finding> findings) =>
        findings.Select(f => string.Join(
            "|",
            new[] { f.Location }.Concat(f.RelatedLocations).Select(l => $"{Path.GetFileName(l.FilePath)}:{l.StartLine}:{l.EndLine}"))).ToList();

    [Fact]
    public async Task CommandReportsTheCloneInAProject()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        directory.Write("A.cs", "using System.Collections.Generic;\n" + Class("A", Statements("a", 1)));
        directory.Write("B.cs", "using System.Collections.Generic;\n" + Class("B", Statements("b", 2)));

        var output = new StringWriter();
        var exitCode = await NeolinterCommand.RunAsync(["analyze", "duplicates", "App.csproj", "--format", "text"], output, new StringWriter(), directory.Path);

        Assert.Equal(ExitCodes.Findings, exitCode);
        Assert.Contains("NLC0001", output.ToString());
        Assert.Contains("related:", output.ToString());
    }

    [Fact]
    public async Task CommandReportsThePlaygroundGroup()
    {
        var output = new StringWriter();
        var exitCode = await NeolinterCommand.RunAsync(["analyze", "duplicates", "samples/Playground", "--format", "text"], output, new StringWriter(), TestPaths.RepositoryRoot);

        Assert.Equal(ExitCodes.Findings, exitCode);
        var report = output.ToString();
        Assert.Equal(1, report.Split("NLC0001", StringSplitOptions.None).Length - 1);
        Assert.Contains("DuplicateExamples.cs", report);
        Assert.DoesNotContain("NotReported", report);
    }

    // Performance smoke test on a medium solution: this repository.
    [Fact]
    public async Task AnalysesThisRepositoryInReasonableTime()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var exitCode = await NeolinterCommand.RunAsync(["analyze", "duplicates", "Neolinter.slnx"], new StringWriter(), new StringWriter(), TestPaths.RepositoryRoot);

        Assert.NotEqual(ExitCodes.Error, exitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(2), $"Took {stopwatch.Elapsed}.");
    }
}
