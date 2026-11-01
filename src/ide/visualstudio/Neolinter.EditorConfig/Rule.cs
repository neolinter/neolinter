using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Neolinter.EditorConfig;

public sealed class RuleOption
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "string";
    public string Default { get; set; } = "";
    public int? Minimum { get; set; }
    public List<string> Values { get; set; } = new();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>A rule as described in /rules/*.yaml.</summary>
public sealed class Rule
{
    public static readonly string[] Severities = { "none", "silent", "suggestion", "warning", "error" };

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Language { get; set; } = "";
    public string Type { get; set; } = "";
    public string Category { get; set; } = "";
    public string DefaultSeverity { get; set; } = "";
    public string Description { get; set; } = "";
    public List<RuleOption> Options { get; set; } = new();

    public string SeverityKey => $"dotnet_diagnostic.{Id}.severity";

    /// <summary>Rules embedded from /rules at build time, ordered by file name.</summary>
    public static IReadOnlyList<Rule> LoadEmbedded()
    {
        var assembly = typeof(Rule).Assembly;
        var yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("rules/", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
                var rule = yaml.Deserialize<Rule>(reader);
                return rule.Id.Length == 0 || rule.Title.Length == 0 || rule.Type.Length == 0 || rule.DefaultSeverity.Length == 0
                    ? throw new InvalidDataException($"{name}: id, title, type and defaultSeverity are required")
                    : rule;
            })
            .ToList();
    }
}
