using Neolinter.Cli.Core;

namespace Neolinter.Cli.Tests;

public class SolutionResolveTests
{
    [Fact]
    public void TopLevelSolutionWinsOverSubfolders()
    {
        using var directory = new TestPaths.TempDirectory();
        var top = directory.Write("Top.slnx", "");
        directory.Write(Path.Combine("sub", "Other.slnx"), "");

        Assert.Equal(top, SolutionLoader.Resolve(null, directory.Path));
    }

    [Fact]
    public void FindsTheSingleSolutionInASubfolder()
    {
        using var directory = new TestPaths.TempDirectory();
        var solution = directory.Write(Path.Combine("modules", "app", "App.sln"), "");

        Assert.Equal(solution, SolutionLoader.Resolve(null, directory.Path));
    }

    [Fact]
    public void FindsTheSolutionInASubfolderOfAGivenDirectory()
    {
        using var directory = new TestPaths.TempDirectory();
        var solution = directory.Write(Path.Combine("repo", "src", "App.slnx"), "");

        Assert.Equal(solution, SolutionLoader.Resolve("repo", directory.Path));
    }

    [Fact]
    public void SeveralSolutionsInSubfoldersAreListed()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write(Path.Combine("a", "A.slnx"), "");
        directory.Write(Path.Combine("b", "c", "B.sln"), "");

        var error = Assert.Throws<FileNotFoundException>(() => SolutionLoader.Resolve(null, directory.Path));

        Assert.Contains(Path.Combine("a", "A.slnx"), error.Message);
        Assert.Contains(Path.Combine("b", "c", "B.sln"), error.Message);
    }

    [Fact]
    public void NoSolutionKeepsTheErrorAndMentionsSubfolders()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write(Path.Combine("sub", "App.csproj"), "");

        var error = Assert.Throws<FileNotFoundException>(() => SolutionLoader.Resolve(null, directory.Path));

        Assert.Contains("No .sln or .slnx file", error.Message);
    }

    [Theory]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".hidden")]
    [InlineData(".git")]
    public void IgnoredFoldersAreNotSearched(string folder)
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write($"{folder}/Ignored.slnx", "");

        Assert.Throws<FileNotFoundException>(() => SolutionLoader.Resolve(null, directory.Path));
    }

    [Fact]
    public void SearchStopsAtThreeLevels()
    {
        using var directory = new TestPaths.TempDirectory();
        var near = directory.Write(Path.Combine("a", "b", "c", "Near.slnx"), "");
        directory.Write(Path.Combine("a", "b", "c", "d", "Far.slnx"), "");

        Assert.Equal(near, SolutionLoader.Resolve(null, directory.Path));
    }

    [Fact]
    public void ASolutionBeyondThreeLevelsIsNotFound()
    {
        using var directory = new TestPaths.TempDirectory();
        directory.Write(Path.Combine("a", "b", "c", "d", "Far.slnx"), "");

        Assert.Throws<FileNotFoundException>(() => SolutionLoader.Resolve(null, directory.Path));
    }
}
