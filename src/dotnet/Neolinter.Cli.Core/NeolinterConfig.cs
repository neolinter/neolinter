using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neolinter.Cli.Core;

/// <summary>Content of neolinter.json. The format is a public contract: see docs/architecture.md.</summary>
public sealed class NeolinterConfig
{
    public const string FileName = "neolinter.json";
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public DeadCodeConfig DeadCode { get; init; } = new();

    public DuplicatesConfig Duplicates { get; init; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Typos in a configuration file must fail loudly rather than be ignored.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Loads the file, or returns the defaults when it does not exist.</summary>
    /// <exception cref="ConfigException">The file exists but is invalid.</exception>
    public static NeolinterConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new NeolinterConfig();
        }

        NeolinterConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<NeolinterConfig>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new ConfigException($"{path}: {exception.Message}", exception);
        }

        if (config is null)
        {
            throw new ConfigException($"{path}: the file must contain a JSON object.");
        }

        config.Validate(path);
        return config;
    }

    private void Validate(string path)
    {
        if (Version != CurrentVersion)
        {
            throw new ConfigException($"{path}: unsupported version {Version}, expected {CurrentVersion}.");
        }

        if (Duplicates.MinStatements < 2)
        {
            throw new ConfigException($"{path}: duplicates.minStatements must be at least 2.");
        }

        if (Duplicates.MinTokens < 0)
        {
            throw new ConfigException($"{path}: duplicates.minTokens must not be negative.");
        }
    }
}

public sealed class DeadCodeConfig
{
    /// <summary>Members or types carrying one of these attributes (full name) are never reported, e.g. serialization or DI markers.</summary>
    public IReadOnlyList<string> ExcludeAttributes { get; init; } = [];

    /// <summary>Types deriving from or implementing these types (full name) are used by convention, e.g. controllers or message handlers.</summary>
    public IReadOnlyList<string> EntryPointBaseTypes { get; init; } = [];

    /// <summary>Glob patterns, relative to the solution directory, of files that are never reported.</summary>
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];

    /// <summary>When false, public members of packable libraries are treated as used by external callers.</summary>
    public bool IncludePublicApi { get; init; }

    /// <summary>When false, a member referenced only from test projects is reported.</summary>
    public bool CountTestReferences { get; init; } = true;

    /// <summary>Glob patterns of the test project files.</summary>
    public IReadOnlyList<string> TestProjects { get; init; } = ["**/*.Tests.csproj", "**/*.Test.csproj"];
}

public sealed class DuplicatesConfig
{
    /// <summary>Smallest number of consecutive statements reported as a duplicate.</summary>
    public int MinStatements { get; init; } = 8;

    /// <summary>Smallest number of tokens of a reported sequence; skips runs of trivial statements. 0 turns the check off.</summary>
    public int MinTokens { get; init; } = 50;

    /// <summary>Glob patterns, relative to the solution directory, of files that are ignored.</summary>
    public IReadOnlyList<string> ExcludePaths { get; init; } = [];

    /// <summary>Ignores generated code: *.g.cs, *.designer.cs and members marked [GeneratedCode].</summary>
    public bool ExcludeGenerated { get; init; } = true;
}

public sealed class ConfigException(string message, Exception? innerException = null) : Exception(message, innerException);
