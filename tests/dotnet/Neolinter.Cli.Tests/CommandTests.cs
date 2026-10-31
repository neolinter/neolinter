using Neolinter.Cli.Core;

namespace Neolinter.Cli.Tests;

public class CommandTests
{
    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string currentDirectory, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = await NeolinterCommand.RunAsync(args, output, error, currentDirectory);
        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task MissingCommandIsAnError()
    {
        var (exitCode, _, error) = await RunAsync(Path.GetTempPath());
        Assert.Equal(ExitCodes.Error, exitCode);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task UnknownOptionIsAnError()
    {
        var (exitCode, _, _) = await RunAsync(Path.GetTempPath(), "analyze", "dead-code", "--nope");
        Assert.Equal(ExitCodes.Error, exitCode);
    }

    [Fact]
    public async Task HelpSucceeds()
    {
        var (exitCode, output, _) = await RunAsync(Path.GetTempPath(), "analyze", "--help");
        Assert.Equal(ExitCodes.Ok, exitCode);
        Assert.Contains("dead-code", output);
        Assert.Contains("duplicates", output);
    }

    [Theory]
    [InlineData("dead-code")]
    [InlineData("duplicates")]
    public async Task MissingSolutionIsAnError(string analysis)
    {
        using var directory = new TestPaths.TempDirectory();
        var (exitCode, _, error) = await RunAsync(directory.Path, "analyze", analysis);
        Assert.Equal(ExitCodes.Error, exitCode);
        Assert.Contains("No .sln or .slnx file", error);
    }

    [Fact]
    public async Task InvalidConfigIsAnError()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write("App.slnx", "<Solution />");
        directory.Write(NeolinterConfig.FileName, """{ "unknown": true }""");

        var (exitCode, _, error) = await RunAsync(directory.Path, "analyze", "duplicates");

        Assert.Equal(ExitCodes.Error, exitCode);
        Assert.Contains(NeolinterConfig.FileName, error);
    }

    [Fact]
    public async Task DuplicatesLoadsTheProjectThenReportsNotImplemented()
    {
        using var directory = new TestPaths.TempDirectory();
        var project = WriteProject(directory);

        var (exitCode, _, error) = await RunAsync(directory.Path, "analyze", "duplicates", project);

        Assert.Equal(ExitCodes.Error, exitCode);
        Assert.Contains("Loaded 1 project(s)", error);
        Assert.Contains("not implemented", error);
    }

    [Fact]
    public async Task SolutionLoaderOpensProjectDocuments()
    {
        using var directory = new TestPaths.TempDirectory();
        var project = WriteProject(directory);

        using var loaded = await SolutionLoader.LoadAsync(project);

        var documents = loaded.Solution.Projects.Single().Documents.Select(d => d.Name);
        Assert.Contains("Greeter.cs", documents);
    }

    private static string WriteProject(TestPaths.TempDirectory directory)
    {
        directory.Write("Greeter.cs", "public class Greeter { public string Hello() => \"hi\"; }");
        return directory.Write("Sample.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
    }
}
