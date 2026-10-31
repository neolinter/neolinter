using System.Text.Json;
using Neolinter.Cli.Core;

namespace Neolinter.Cli.Tests;

public class OutputOptionsTests
{
    private static AnalysisReport Report(TestPaths.TempDirectory directory, params Finding[] findings) =>
        new("1.0.0", directory.Path, CliRules.All, findings);

    private static Finding At(TestPaths.TempDirectory directory, string file, int line, string ruleId = "NLD0001", FindingLevel level = FindingLevel.Warning) =>
        new(ruleId, "message", level, new SourceLocation(Path.Combine(directory.Path, file), line, 5, line, 9));

    [Fact]
    public void FingerprintIgnoresLineShiftsAndWhitespace()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write("A.cs", "class A\n{\n    void M() { }\n}\n");
        var before = Fingerprints.Compute(Report(directory, At(directory, "A.cs", 3)));

        directory.Write("A.cs", "// new\n\nclass A\n{\n\tvoid   M() { }\n}\n");
        var after = Fingerprints.Compute(Report(directory, At(directory, "A.cs", 5)));

        Assert.Equal(before, after);
    }

    [Fact]
    public void FingerprintDependsOnRuleFileTextAndOccurrence()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write("A.cs", "int x;\nint x;\nint y;\n");
        directory.Write("B.cs", "int x;\n");

        var fingerprints = Fingerprints.Compute(Report(
            directory,
            At(directory, "A.cs", 1),
            At(directory, "A.cs", 2), // same text: told apart by occurrence index
            At(directory, "A.cs", 3), // other text
            At(directory, "B.cs", 1), // other file
            At(directory, "A.cs", 1, "NLC0001"))); // other rule

        Assert.Equal(5, fingerprints.Distinct().Count());
    }

    [Fact]
    public void SarifCarriesTheFingerprint()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write("A.cs", "int x;\n");
        var report = Report(directory, At(directory, "A.cs", 1));

        var result = JsonDocument.Parse(SarifWriter.Write(report)).RootElement.GetProperty("runs")[0].GetProperty("results")[0];

        Assert.Equal(Fingerprints.Compute(report)[0], result.GetProperty("partialFingerprints").GetProperty(Fingerprints.Key).GetString());
    }

    [Fact]
    public void BaselineDropsKnownFindingsAndKeepsNewOnes()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write("A.cs", "int x;\nint y;\n");
        var path = directory.Write("baseline.sarif", SarifWriter.Write(Report(directory, At(directory, "A.cs", 1))));

        var remaining = Baseline.Load(path).Filter(Report(directory, At(directory, "A.cs", 1), At(directory, "A.cs", 2)));

        Assert.Equal([2], remaining.Select(f => f.Location.StartLine));
    }

    [Fact]
    public void BaselineWithoutFingerprintsMatchesNothingAndInvalidJsonIsAnError()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write("A.cs", "int x;\n");
        var report = Report(directory, At(directory, "A.cs", 1));

        var empty = directory.Write("empty.sarif", """{ "version": "2.1.0", "runs": [ { "results": [ { "ruleId": "NLD0001" } ] } ] }""");
        Assert.Single(Baseline.Load(empty).Filter(report));

        var invalid = directory.Write("invalid.sarif", "not json");
        Assert.Throws<ConfigException>(() => Baseline.Load(invalid));
    }

    [Fact]
    public void TextReportListsOneLinePerFindingWithRelatedLocations()
    {
        using var directory = new TestPaths.TempDirectory();
        var location = new SourceLocation(Path.Combine(directory.Path, "src", "A.cs"), 3, 7, 3, 9);
        var related = new SourceLocation(Path.Combine(directory.Path, "B.cs"), 10, 1, 12, 2);
        var report = Report(directory, new Finding("NLC0001", "Duplicated.", FindingLevel.Note, location, [related]));

        Assert.Equal("src/A.cs(3,7): note NLC0001: Duplicated.\n  related: B.cs(10,1)\n", TextReportWriter.Write(report));
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string directory, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await NeolinterCommand.RunAsync(args, output, error, directory);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Theory]
    [InlineData("--fail-on", "fatal")]
    [InlineData("--format", "xml")]
    public async Task InvalidOptionValuesAreAnError(string option, string value)
    {
        var (exitCode, _, _) = await RunAsync(Path.GetTempPath(), "analyze", "dead-code", option, value);
        Assert.Equal(ExitCodes.Error, exitCode);
    }

    [Fact]
    public async Task MissingBaselineFileIsAnError()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write("App.slnx", "<Solution />");

        var (exitCode, _, error) = await RunAsync(directory.Path, "analyze", "dead-code", "--baseline", "nope.sarif");

        Assert.Equal(ExitCodes.Error, exitCode);
        Assert.Contains("nope.sarif", error);
    }

    [Fact]
    public async Task CommandAppliesFormatBaselineAndFailOn()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write("App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        directory.Write("Old.cs", "public class OldUnused { }");
        directory.Write(NeolinterConfig.FileName, """{ "deadCode": { "includePublicApi": true } }""");
        directory.Write("App.slnx", """<Solution><Project Path="App.csproj" /></Solution>""");
        await RestoreAsync(Path.Combine(directory.Path, "App.csproj"));

        var first = await RunAsync(directory.Path, "analyze", "dead-code", "-o", "baseline.sarif");
        Assert.Equal(ExitCodes.Findings, first.ExitCode);

        // Nothing new: no output, exit 0.
        var unchanged = await RunAsync(directory.Path, "analyze", "dead-code", "--format", "text", "--baseline", "baseline.sarif");
        Assert.Equal(ExitCodes.Ok, unchanged.ExitCode);
        Assert.Empty(unchanged.Output);
        Assert.Contains("1 finding(s) already in the baseline.", unchanged.Error);

        // A new finding is reported as text and fails the run.
        directory.Write("New.cs", "public class NewUnused { }");
        var added = await RunAsync(directory.Path, "analyze", "dead-code", "--format", "text", "--baseline", "baseline.sarif");
        Assert.Equal(ExitCodes.Findings, added.ExitCode);
        Assert.Contains("New.cs(1,14): warning NLD0001:", added.Output);
        Assert.DoesNotContain("OldUnused", added.Output);

        // Findings below the --fail-on level are reported but keep exit code 0.
        var lenient = await RunAsync(directory.Path, "analyze", "dead-code", "--format", "text", "--fail-on", "error");
        Assert.Equal(ExitCodes.Ok, lenient.ExitCode);
        Assert.Contains("OldUnused", lenient.Output);
    }

    private static async Task RestoreAsync(string project)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("restore");
        start.ArgumentList.Add(project);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, output);
    }
}
