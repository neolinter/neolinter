using System.Text.Json;
using Neolinter.Cli.Core;

namespace Neolinter.Cli.Tests;

public class SarifWriterTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "neolinter-sarif");

    /// <summary>Fake results: the CLI does not detect anything yet.</summary>
    private static AnalysisReport SampleReport(params Finding[] findings) =>
        new("1.2.3", Root, CliRules.All, findings);

    private static Finding UnusedMember(string relativePath) => new(
        "NLC0002",
        "'Helper.Unused()' is never referenced.",
        FindingLevel.Warning,
        new SourceLocation(Path.Combine(Root, relativePath), 10, 5, 10, 25));

    private static JsonElement Run(AnalysisReport report) => JsonDocument.Parse(SarifWriter.Write(report)).RootElement.GetProperty("runs")[0];

    [Fact]
    public void WritesSarif210Header()
    {
        var log = JsonDocument.Parse(SarifWriter.Write(SampleReport())).RootElement;

        Assert.Equal("2.1.0", log.GetProperty("version").GetString());
        Assert.Equal(SarifWriter.SchemaUri, log.GetProperty("$schema").GetString());
        Assert.Equal(1, log.GetProperty("runs").GetArrayLength());
    }

    [Fact]
    public void DescribesToolAndRules()
    {
        var driver = Run(SampleReport()).GetProperty("tool").GetProperty("driver");

        Assert.Equal("Neolinter", driver.GetProperty("name").GetString());
        Assert.Equal("1.2.3", driver.GetProperty("version").GetString());
        var rules = driver.GetProperty("rules");
        Assert.Equal(["NLC0002", "NLC0003"], rules.EnumerateArray().Select(r => r.GetProperty("id").GetString()));
        Assert.Equal("warning", rules[0].GetProperty("defaultConfiguration").GetProperty("level").GetString());
        Assert.False(string.IsNullOrEmpty(rules[0].GetProperty("shortDescription").GetProperty("text").GetString()));
    }

    [Fact]
    public void EmptyResultsAreAnExplicitEmptyArray()
    {
        Assert.Equal(0, Run(SampleReport()).GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void WritesResultWithRelativeEscapedUri()
    {
        var run = Run(SampleReport(UnusedMember(Path.Combine("src", "My App", "Helper.cs"))));
        var result = run.GetProperty("results")[0];

        Assert.Equal("NLC0002", result.GetProperty("ruleId").GetString());
        Assert.Equal(0, result.GetProperty("ruleIndex").GetInt32());
        Assert.Equal("warning", result.GetProperty("level").GetString());
        Assert.Equal("'Helper.Unused()' is never referenced.", result.GetProperty("message").GetProperty("text").GetString());

        var physical = result.GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal("src/My%20App/Helper.cs", physical.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal("SRCROOT", physical.GetProperty("artifactLocation").GetProperty("uriBaseId").GetString());
        var region = physical.GetProperty("region");
        Assert.Equal([10, 5, 10, 25], new[] { "startLine", "startColumn", "endLine", "endColumn" }.Select(p => region.GetProperty(p).GetInt32()));

        var rootUri = run.GetProperty("originalUriBaseIds").GetProperty("SRCROOT").GetProperty("uri").GetString();
        Assert.StartsWith("file:///", rootUri);
        Assert.EndsWith("/", rootUri);
        Assert.False(result.TryGetProperty("relatedLocations", out _));
    }

    [Fact]
    public void FileOutsideRootGetsAbsoluteUri()
    {
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "Other.cs");
        var finding = UnusedMember("x.cs") with { Location = new SourceLocation(outside, 1, 1, 1, 2) };

        var artifact = Run(SampleReport(finding)).GetProperty("results")[0].GetProperty("locations")[0]
            .GetProperty("physicalLocation").GetProperty("artifactLocation");

        Assert.Equal(new Uri(outside).AbsoluteUri, artifact.GetProperty("uri").GetString());
        Assert.False(artifact.TryGetProperty("uriBaseId", out _));
    }

    [Fact]
    public void DuplicateListsTheOtherCopiesAsRelatedLocations()
    {
        var finding = new Finding(
            "NLC0003",
            "These 9 statements are repeated in 2 other places.",
            FindingLevel.Note,
            new SourceLocation(Path.Combine(Root, "A.cs"), 3, 9, 14, 10),
            [new SourceLocation(Path.Combine(Root, "B.cs"), 20, 9, 31, 10), new SourceLocation(Path.Combine(Root, "C.cs"), 5, 9, 16, 10)]);

        var result = Run(SampleReport(finding)).GetProperty("results")[0];

        Assert.Equal(1, result.GetProperty("ruleIndex").GetInt32());
        Assert.Equal("note", result.GetProperty("level").GetString());
        var related = result.GetProperty("relatedLocations");
        Assert.Equal(2, related.GetArrayLength());
        Assert.Equal(1, related[1].GetProperty("id").GetInt32());
        Assert.Equal("C.cs", related[1].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
    }

    [Fact]
    public void RejectsFindingOfUnknownRule()
    {
        var finding = UnusedMember("x.cs") with { RuleId = "NL9999" };
        Assert.Throws<ArgumentException>(() => SarifWriter.Write(SampleReport(finding)));
    }

    [Fact]
    public async Task StreamAndStringOutputsAreIdentical()
    {
        var report = SampleReport(UnusedMember("x.cs"));
        using var stream = new MemoryStream();
        await SarifWriter.WriteAsync(stream, report);
        Assert.Equal(SarifWriter.Write(report), System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void EveryCliRuleHasARuleFile()
    {
        var rulesDirectory = Path.Combine(TestPaths.RepositoryRoot, "rules");
        Assert.All(CliRules.All, rule => Assert.True(File.Exists(Path.Combine(rulesDirectory, rule.Id + ".yaml")), rule.Id));
    }
}
