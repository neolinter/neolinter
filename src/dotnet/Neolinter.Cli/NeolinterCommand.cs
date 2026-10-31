using System.CommandLine;
using System.Reflection;
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
        analyze.Subcommands.Add(CreateAnalysis("dead-code", "Report public and internal members that nothing in the solution references.", output, error, currentDirectory));
        analyze.Subcommands.Add(CreateAnalysis("duplicates", "Report sequences of statements repeated across the solution.", output, error, currentDirectory));

        return new RootCommand("Neolinter command line: solution-wide analyses that are too expensive to run while typing.")
        {
            Subcommands = { analyze },
        };
    }

    private static Command CreateAnalysis(string name, string description, TextWriter output, TextWriter error, string currentDirectory)
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

                _ = NeolinterConfig.Load(configPath);

                using var loaded = await SolutionLoader.LoadAsync(solutionPath, cancellationToken).ConfigureAwait(false);
                foreach (var warning in loaded.Warnings)
                {
                    await error.WriteLineAsync($"warning: {warning}").ConfigureAwait(false);
                }

                await output.WriteLineAsync($"Loaded {loaded.Solution.Projects.Count()} project(s) from {solutionPath}.").ConfigureAwait(false);
                await error.WriteLineAsync($"The {name} analysis is not implemented yet.").ConfigureAwait(false);
                return ExitCodes.Error;
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
