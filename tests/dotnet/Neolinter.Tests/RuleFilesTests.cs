using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Neolinter.Analyzers.InvertIf;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Neolinter.Tests;

/// <summary>Keeps /rules/*.yaml (read by the IDE settings screens) in sync with the analyzers.</summary>
public class RuleFilesTests
{
    private static readonly DiagnosticAnalyzer[] Analyzers = [new InvertIfAnalyzer()];

    public static TheoryData<string> RealtimeRuleIds() => new(LoadRules().Where(r => r.Type == "realtime").Select(r => r.Id));

    [Theory]
    [MemberData(nameof(RealtimeRuleIds))]
    public void RealtimeRuleMatchesDescriptor(string id)
    {
        var rule = LoadRules().Single(r => r.Id == id);
        var descriptor = Analyzers.SelectMany(a => a.SupportedDiagnostics).SingleOrDefault(d => d.Id == id);

        Assert.NotNull(descriptor);
        Assert.Equal(rule.Title, descriptor.Title.ToString());
        Assert.Equal(rule.DefaultSeverity, ToEditorConfigSeverity(descriptor.DefaultSeverity));
        Assert.Equal("csharp", rule.Language);
    }

    [Fact]
    public void EveryDescriptorHasARuleFile()
    {
        var ids = LoadRules().Select(r => r.Id).ToHashSet();
        Assert.All(Analyzers.SelectMany(a => a.SupportedDiagnostics), d => Assert.Contains(d.Id, ids));
    }

    [Fact]
    public void RuleFilesAreWellFormed()
    {
        var severities = new[] { "none", "silent", "suggestion", "warning", "error" };
        var optionTypes = new[] { "integer", "boolean", "string", "enum" };

        Assert.All(LoadRules(), rule =>
        {
            Assert.Matches("^(NL|NLD|NLC)[0-9]{4}$", rule.Id);
            Assert.Contains(rule.Type, new[] { "realtime", "cli" });
            Assert.Contains(rule.DefaultSeverity, severities);
            Assert.False(string.IsNullOrWhiteSpace(rule.Title));
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
            Assert.All(rule.Options, option =>
            {
                Assert.StartsWith(rule.Id.ToLowerInvariant() + ".", option.Key);
                Assert.Contains(option.Type, optionTypes);
            });
        });
    }

    private static string ToEditorConfigSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Hidden => "silent",
        DiagnosticSeverity.Info => "suggestion",
        DiagnosticSeverity.Warning => "warning",
        _ => "error",
    };

    private static List<RuleFile> LoadRules()
    {
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        return Directory.GetFiles(Path.Combine(RepositoryRoot(), "rules"), "*.yaml")
            .Select(path => deserializer.Deserialize<RuleFile>(File.ReadAllText(path)))
            .ToList();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Neolinter.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    public sealed class RuleFile
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Language { get; set; } = "";
        public string Type { get; set; } = "";
        public string Category { get; set; } = "";
        public string DefaultSeverity { get; set; } = "";
        public string Description { get; set; } = "";
        public List<RuleOption> Options { get; set; } = [];
    }

    public sealed class RuleOption
    {
        public string Key { get; set; } = "";
        public string Type { get; set; } = "";
        public object? Default { get; set; }
        public int? Minimum { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Values { get; set; } = [];
    }
}
