using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.ProjectSystem.Query;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;
using Neolinter.EditorConfig;

namespace Neolinter.VisualStudio;

[VisualStudioContribution]
internal sealed class SettingsToolWindow(VisualStudioExtensibility extensibility) : ToolWindow(extensibility)
{
    private SettingsControl? content;

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
    };

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Title = "Neolinter";
        var solutionPath = (await Extensibility.Workspaces().QuerySolutionAsync(s => s.With(x => x.Path), cancellationToken)).FirstOrDefault()?.Path;
        var projectPaths = (await Extensibility.Workspaces().QueryProjectsAsync(p => p.With(x => x.Path), cancellationToken)).Select(p => p.Path);
        content = new SettingsControl(new SettingsViewModel(EditorConfigLocation(solutionPath), PackageBanner(solutionPath, projectPaths)));
    }

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IRemoteUserControl>(content!);

    private static string? EditorConfigLocation(string? solutionPath) =>
        string.IsNullOrEmpty(solutionPath) ? null : Path.Combine(Path.GetDirectoryName(solutionPath)!, ".editorconfig");

    // Looks in the project files and in Directory.Build.props next to the solution, like the VS Code extension.
    private static string PackageBanner(string? solutionPath, IEnumerable<string?> projectPaths)
    {
        var projects = projectPaths.Where(p => p is not null && p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).Select(p => p!).ToList();
        if (projects.Count == 0 || solutionPath is null)
        {
            return string.Empty;
        }

        var files = projects.Append(Path.Combine(Path.GetDirectoryName(solutionPath)!, "Directory.Build.props")).Where(File.Exists);
        return files.Any(f => NuGetDetection.ReferencesNeolinter(File.ReadAllText(f)))
            ? string.Empty
            : $"The {NuGetDetection.PackageId} package is not referenced by this solution, so the rules below do nothing yet. Run: dotnet add package {NuGetDetection.PackageId}";
    }
}
