using System.CommandLine;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Neolinter.Cli.Core;

namespace Neolinter.Cli;

/// <summary>
/// Command line definition: <c>neolinter analyze dead-code|duplicates [path] [--config file] [--output file.sarif]</c>.
/// Exit codes follow <see cref="ExitCodes"/>.
/// </summary>
internal static class NeolinterCommand
{
    public static string Version { get; } =
        typeof(NeolinterCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, string currentDirectory, CancellationToken cancellationToken = default)
    {
        var parseResult = Create(output, error, currentDirectory).Parse(args);

        // System.CommandLine returns 1 on parse errors, which our contract reserves for findings.
        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                await error.WriteLineAsync(parseError.Message).ConfigureAwait(false);
            }

            return ExitCodes.Error;
        }

        return await parseResult.InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken).ConfigureAwait(false);
    }

    private static RootCommand Create(TextWriter output, TextWriter error, string currentDirectory)
    {
        var analyze = new Command("analyze", "Run a solution-wide analysis on demand and report the results as SARIF 2.1.0.");
        analyze.Subcommands.Add(CreateAnalysis(
            "dead-code",
            "Report types and members that nothing in the solution references.",
            (solution, config, root, ct) => DeadCodeAnalyzer.AnalyzeAsync(solution, config.DeadCode, root, ct),
            output,
            error,
            currentDirectory));
        analyze.Subcommands.Add(CreateAnalysis("duplicates", "Report sequences of statements repeated across the solution.", null, output, error, currentDirectory));

        return new RootCommand("Neolinter command line: solution-wide analyses that are too expensive to run while typing.")
        {
            Subcommands = { analyze },
        };
    }

    private delegate Task<IReadOnlyList<Finding>> Analysis(Solution solution, NeolinterConfig config, string rootDirectory, CancellationToken cancellationToken);

    /// <param name="analysis">Null while the analysis is not implemented.</param>
    private static Command CreateAnalysis(string name, string description, Analysis? analysis, TextWriter output, TextWriter error, string currentDirectory)
    {
        var path = new Argument<string?>("path")
        {
            Description = "Solution (.sln, .slnx) or project (.csproj) to analyse. Defaults to the single solution of the current directory.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var config = new Option<string?>("--config")
        {
            Description = $"Configuration file. Defaults to {NeolinterConfig.FileName} next to the solution.",
        };
        var sarifOutput = new Option<string?>("--output", "-o")
        {
            Description = "SARIF file to write. Defaults to the standard output.",
        };

        var command = new Command(name, description) { path, config, sarifOutput };
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                var solutionPath = SolutionLoader.Resolve(parseResult.GetValue(path), currentDirectory);
                var configPath = parseResult.GetValue(config) is { } explicitConfig
                    ? Path.GetFullPath(explicitConfig, currentDirectory)
                    : Path.Combine(Path.GetDirectoryName(solutionPath)!, NeolinterConfig.FileName);
                if (parseResult.GetValue(config) is not null && !File.Exists(configPath))
                {
                    throw new FileNotFoundException($"Configuration file '{configPath}' does not exist.");
                }

                var neolinterConfig = NeolinterConfig.Load(configPath);

                using var loaded = await SolutionLoader.LoadAsync(solutionPath, cancellationToken).ConfigureAwait(false);
                foreach (var warning in loaded.Warnings)
                {
                    await error.WriteLineAsync($"warning: {warning}").ConfigureAwait(false);
                }

                // Progress goes to the error stream: the standard output may carry the SARIF log.
                await error.WriteLineAsync($"Loaded {loaded.Solution.Projects.Count()} project(s) from {solutionPath}.").ConfigureAwait(false);
                if (analysis is null)
                {
                    await error.WriteLineAsync($"The {name} analysis is not implemented yet.").ConfigureAwait(false);
                    return ExitCodes.Error;
                }

                var root = Path.GetDirectoryName(solutionPath)!;
                var findings = await analysis(loaded.Solution, neolinterConfig, root, cancellationToken).ConfigureAwait(false);
                var report = new AnalysisReport(Version, root, CliRules.All, findings);
                if (parseResult.GetValue(sarifOutput) is { } outputPath)
                {
                    await using var stream = File.Create(Path.GetFullPath(outputPath, currentDirectory));
                    await SarifWriter.WriteAsync(stream, report, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await output.WriteLineAsync(SarifWriter.Write(report)).ConfigureAwait(false);
                }

                await error.WriteLineAsync($"{findings.Count} finding(s).").ConfigureAwait(false);
                return findings.Count == 0 ? ExitCodes.Ok : ExitCodes.Findings;
            }
            catch (Exception exception) when (exception is FileNotFoundException or ConfigException or ArgumentException or InvalidOperationException)
            {
                await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
                return ExitCodes.Error;
            }
        });
        return command;
    }
}
