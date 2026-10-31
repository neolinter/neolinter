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

    /// <summary>
    /// Finds the solution to analyse: the given path, or the single .sln/.slnx of the directory.
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

        var candidates = Directory.EnumerateFiles(currentDirectory)
            .Where(f => SolutionExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        return candidates.Count == 1
            ? candidates[0]
            : throw new FileNotFoundException(candidates.Count == 0
                ? $"No .sln or .slnx file in '{currentDirectory}'. Pass the solution or project path."
                : $"Several solutions in '{currentDirectory}'. Pass the one to analyse.");
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
