using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neolinter.Cli.Core;

/// <summary>Writes an <see cref="AnalysisReport"/> as a SARIF 2.1.0 log with a single run.</summary>
public static class SarifWriter
{
    public const string SchemaUri = "https://json.schemastore.org/sarif-2.1.0.json";
    private const string RootBaseId = "SRCROOT";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteAsync(Stream stream, AnalysisReport report, CancellationToken cancellationToken = default) =>
        await JsonSerializer.SerializeAsync(stream, ToSarif(report), JsonOptions, cancellationToken).ConfigureAwait(false);

    public static string Write(AnalysisReport report) => JsonSerializer.Serialize(ToSarif(report), JsonOptions);

    private static object ToSarif(AnalysisReport report)
    {
        var root = Path.GetFullPath(report.RootDirectory);
        var ruleIndex = report.Rules.Select((rule, index) => (rule.Id, index)).ToDictionary(x => x.Id, x => x.index);

        var unknown = report.Findings.Select(f => f.RuleId).FirstOrDefault(id => !ruleIndex.ContainsKey(id));
        if (unknown is not null)
        {
            throw new ArgumentException($"Finding uses rule {unknown}, which is not part of the report rules.", nameof(report));
        }

        var fingerprints = Fingerprints.Compute(report);

        return new Dictionary<string, object>
        {
            ["$schema"] = SchemaUri,
            ["version"] = "2.1.0",
            ["runs"] = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "Neolinter",
                            version = report.ToolVersion,
                            informationUri = "https://github.com/neolinter/neolinter",
                            rules = report.Rules.Select(rule => new
                            {
                                id = rule.Id,
                                name = rule.Name,
                                shortDescription = new { text = rule.ShortDescription },
                                helpUri = rule.HelpUri,
                                defaultConfiguration = new { level = Level(rule.DefaultLevel) },
                            }),
                        },
                    },
                    originalUriBaseIds = new Dictionary<string, object>
                    {
                        [RootBaseId] = new { uri = new Uri(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar).AbsoluteUri },
                    },
                    columnKind = "utf16CodeUnits",
                    results = report.Findings.Select((finding, index) => new
                    {
                        ruleId = finding.RuleId,
                        ruleIndex = ruleIndex[finding.RuleId],
                        level = Level(finding.Level),
                        message = new { text = finding.Message },
                        locations = new[] { new { physicalLocation = PhysicalLocation(finding.Location, root) } },
                        partialFingerprints = new Dictionary<string, string> { [Fingerprints.Key] = fingerprints[index] },
                        relatedLocations = finding.RelatedLocations.Count == 0
                            ? null
                            : finding.RelatedLocations.Select((location, index) => new
                            {
                                id = index,
                                physicalLocation = PhysicalLocation(location, root),
                            }),
                    }),
                },
            },
        };
    }

    private static string Level(FindingLevel level) => level switch
    {
        FindingLevel.Note => "note",
        FindingLevel.Warning => "warning",
        _ => "error",
    };

    private static object PhysicalLocation(SourceLocation location, string root)
    {
        var path = Path.GetFullPath(location.FilePath);
        var relative = Path.GetRelativePath(root, path);
        var insideRoot = !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        return new
        {
            artifactLocation = insideRoot
                ? new { uri = string.Join('/', relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(Uri.EscapeDataString)), uriBaseId = (string?)RootBaseId }
                : new { uri = new Uri(path).AbsoluteUri, uriBaseId = (string?)null },
            region = new
            {
                startLine = location.StartLine,
                startColumn = location.StartColumn,
                endLine = location.EndLine,
                endColumn = location.EndColumn,
            },
        };
    }
}
