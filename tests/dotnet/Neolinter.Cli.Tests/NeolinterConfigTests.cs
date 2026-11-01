using Neolinter.Cli.Core;

namespace Neolinter.Cli.Tests;

public class NeolinterConfigTests
{
    [Fact]
    public void MissingFileGivesDefaults()
    {
        var config = NeolinterConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist", NeolinterConfig.FileName));

        Assert.Equal(8, config.Duplicates.MinStatements);
        Assert.Equal(50, config.Duplicates.MinTokens);
        Assert.True(config.Duplicates.ExcludeGenerated);
        Assert.True(config.DeadCode.CountTestReferences);
        Assert.False(config.DeadCode.IncludePublicApi);
        Assert.Empty(config.DeadCode.ExcludeAttributes);
    }

    [Fact]
    public void ReadsAFullFileWithComments()
    {
        using var directory = new TestPaths.TempDirectory();
        var path = directory.Write(NeolinterConfig.FileName, """
            {
              // Contract version
              "version": 1,
              "deadCode": {
                "excludeAttributes": ["System.Text.Json.Serialization.JsonConstructorAttribute"],
                "entryPointBaseTypes": ["Microsoft.AspNetCore.Mvc.ControllerBase"],
                "excludePaths": ["**/Migrations/**"],
                "includePublicApi": true,
                "countTestReferences": false,
                "testProjects": ["tests/**/*.csproj"],
              },
              "duplicates": { "minStatements": 12, "minTokens": 80, "excludePaths": ["legacy/**"], "excludeGenerated": false }
            }
            """);

        var config = NeolinterConfig.Load(path);

        Assert.Equal(["System.Text.Json.Serialization.JsonConstructorAttribute"], config.DeadCode.ExcludeAttributes);
        Assert.Equal(["Microsoft.AspNetCore.Mvc.ControllerBase"], config.DeadCode.EntryPointBaseTypes);
        Assert.Equal(["**/Migrations/**"], config.DeadCode.ExcludePaths);
        Assert.True(config.DeadCode.IncludePublicApi);
        Assert.False(config.DeadCode.CountTestReferences);
        Assert.Equal(["tests/**/*.csproj"], config.DeadCode.TestProjects);
        Assert.Equal(12, config.Duplicates.MinStatements);
        Assert.Equal(80, config.Duplicates.MinTokens);
        Assert.Equal(["legacy/**"], config.Duplicates.ExcludePaths);
        Assert.False(config.Duplicates.ExcludeGenerated);
    }

    [Theory]
    [InlineData("""{ "duplicates": { "minStatement": 5 } }""")]
    [InlineData("""{ "version": 2 }""")]
    [InlineData("""{ "duplicates": { "minStatements": 1 } }""")]
    [InlineData("""{ "duplicates": { "minTokens": -1 } }""")]
    [InlineData("""{ "deadCode": [] }""")]
    [InlineData("null")]
    [InlineData("not json")]
    public void InvalidFileIsRejected(string content)
    {
        using var directory = new TestPaths.TempDirectory();
        var path = directory.Write(NeolinterConfig.FileName, content);

        var exception = Assert.Throws<ConfigException>(() => NeolinterConfig.Load(path));
        Assert.Contains(path, exception.Message);
    }
}
