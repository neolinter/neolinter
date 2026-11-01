using System.Text.Json;
using Neolinter.EditorConfig;

namespace Neolinter.EditorConfig.Tests;

// Shared with the other IDE implementations, see "Shared .editorconfig fixtures" in docs/architecture.md.
// Adding a case directory needs no code change.
public class FixtureTests
{
    private static readonly string Fixtures = FindFixtures();

    private static string FindFixtures()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "editorconfig");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("tests/fixtures/editorconfig not found above " + AppContext.BaseDirectory);
    }

    public static IEnumerable<object[]> Read() => Cases("read.json");

    public static IEnumerable<object[]> Change() => Cases("change.json");

    private static IEnumerable<object[]> Cases(string marker) =>
        Directory.GetDirectories(Fixtures).Where(d => File.Exists(Path.Combine(d, marker))).Select(d => new object[] { Path.GetFileName(d) }).OrderBy(c => (string)c[0], StringComparer.Ordinal);

    // Bytes matter (CRLF cases): never let the reader normalise anything, and read as UTF-8 without BOM handling.
    private static string File_(string name, string file) => File.ReadAllText(Path.Combine(Fixtures, name, file));

    [Theory]
    [MemberData(nameof(Read))]
    public void ReadCase(string name)
    {
        using var json = JsonDocument.Parse(File_(name, "read.json"));
        var keys = json.RootElement.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).ToList();
        var expected = json.RootElement.GetProperty("expected").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        Assert.Equal(expected.OrderBy(p => p.Key), EditorConfigFile.ReadValues(File_(name, "input.editorconfig"), keys).OrderBy(p => p.Key));
    }

    [Theory]
    [MemberData(nameof(Change))]
    public void ChangeCase(string name)
    {
        using var json = JsonDocument.Parse(File_(name, "change.json"));
        var key = json.RootElement.GetProperty("key").GetString()!;
        var value = json.RootElement.GetProperty("value").GetString()!;
        Assert.Equal(File_(name, "expected.editorconfig"), EditorConfigFile.SetValue(File_(name, "input.editorconfig"), key, value));
    }

    [Fact]
    public void FixturesExist() => Assert.NotEmpty(Read().Concat(Change()));

    [Theory]
    [InlineData("*")]
    [InlineData("*.cs")]
    [InlineData("**.cs")]
    [InlineData("**/*.cs")]
    [InlineData("*.{cs,vb}")]
    [InlineData("**")]
    public void RecognisesCommonCSharpGlobs(string glob) => Assert.True(EditorConfigFile.AppliesToCSharp(glob));

    [Theory]
    [InlineData("*.vb")]
    [InlineData("*.{json,yml}")]
    [InlineData("src/**/*.cs")]
    [InlineData("*.csx")]
    public void IgnoresOtherGlobs(string glob) => Assert.False(EditorConfigFile.AppliesToCSharp(glob));

    [Fact]
    public void LoadsEmbeddedRules()
    {
        var rules = Rule.LoadEmbedded();
        var nlr0001 = Assert.Single(rules, r => r.Id == "NLR0001");
        Assert.Equal("suggestion", nlr0001.DefaultSeverity);
        Assert.Equal("nlr0001.min_statements", Assert.Single(nlr0001.Options).Key);
        Assert.Equal("3", nlr0001.Options[0].Default);
    }

    [Theory]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Neolinter\" Version=\"1\" /></ItemGroup></Project>", true)]
    [InlineData("<!-- <PackageReference Include=\"Neolinter\" /> -->", false)]
    [InlineData("<PackageReference Include=\"Neolinter.Cli\" />", false)]
    public void DetectsPackageReference(string xml, bool expected) => Assert.Equal(expected, NuGetDetection.ReferencesNeolinter(xml));
}
