using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Neolinter.Cli.Core;

/// <summary>A loaded solution. Keep it alive while the solution is used: disposing it releases MSBuild resources.</summary>
public sealed class LoadedSolution(MSBuildWorkspace workspace, Solution solution) : IDisposable
{
    public Solution Solution { get; } = solution;

    /// <summary>Problems reported while loading (missing SDK, failed restore...). Loading may still be partial.</summary>
    public IReadOnlyList<string> Warnings { get; } = workspace.Diagnostics.Select(d => d.Message).ToList();

    public void Dispose() => workspace.Dispose();
}

public static class SolutionLoader
{
    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];
    private static readonly string[] IgnoredFolders = ["node_modules", "bin", "obj"];
    private const int MaxSearchDepth = 3;

    /// <summary>
    /// Finds the solution to analyse: the given path, or the single .sln/.slnx of the directory,
    /// or, when the directory has none, the single one in its subfolders (3 levels deep at most).
    /// </summary>
    /// <exception cref="FileNotFoundException">Nothing usable was found.</exception>
    public static string Resolve(string? path, string currentDirectory)
    {
        if (path is not null)
        {
            var full = Path.GetFullPath(path, currentDirectory);
            if (File.Exists(full))
            {
                return full;
            }

            if (!Directory.Exists(full))
            {
                throw new FileNotFoundException($"'{path}' does not exist.", full);
            }

            currentDirectory = full;
        }

        var candidates = FindSolutions(currentDirectory);
        if (candidates.Count == 0)
        {
            candidates = FindInSubdirectories(currentDirectory);
            return candidates.Count switch
            {
                1 => candidates[0],
                0 => throw new FileNotFoundException($"No .sln or .slnx file in '{currentDirectory}' or its subfolders. Pass the solution or project path."),
                _ => throw new FileNotFoundException(
                    $"Several solutions in the subfolders of '{currentDirectory}': {string.Join(", ", candidates.Select(c => Path.GetRelativePath(currentDirectory, c)))}. Pass the one to analyse."),
            };
        }

        return candidates.Count == 1
            ? candidates[0]
            : throw new FileNotFoundException($"Several solutions in '{currentDirectory}'. Pass the one to analyse.");
    }

    private static List<string> FindSolutions(string directory) =>
        Directory.EnumerateFiles(directory)
            .Where(f => SolutionExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Solutions up to <see cref="MaxSearchDepth"/> levels below the directory, skipping hidden and build folders.</summary>
    private static List<string> FindInSubdirectories(string root)
    {
        var found = new List<string>();
        var level = new List<string> { root };
        for (var depth = 0; depth < MaxSearchDepth && level.Count > 0; depth++)
        {
            level = level
                .SelectMany(Directory.EnumerateDirectories)
                .Where(d => Path.GetFileName(d) is { } name && !name.StartsWith('.') && !IgnoredFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            found.AddRange(level.SelectMany(FindSolutions));
        }

        return found;
    }

    /// <summary>Opens a solution (.sln, .slnx) or a single project (.csproj) with MSBuild.</summary>
    public static async Task<LoadedSolution> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var workspace = MSBuildWorkspace.Create();
        try
        {
            var extension = Path.GetExtension(path);
            var solution = SolutionExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                ? await workspace.OpenSolutionAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false)
                : extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                    ? (await workspace.OpenProjectAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false)).Solution
                    : throw new ArgumentException($"'{path}' is not a .sln, .slnx or .csproj file.", nameof(path));

            return new LoadedSolution(workspace, solution);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }
}
