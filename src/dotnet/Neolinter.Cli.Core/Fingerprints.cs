using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Neolinter.Cli.Core;

/// <summary>
/// Stable identity of a finding, stored in SARIF <c>partialFingerprints</c> and used by baselines.
/// Inputs: rule id, path relative to the report root, whitespace-normalized text of the start line and the
/// occurrence index among identical findings of the file. Line numbers and messages are left out on purpose so
/// that unrelated edits above a finding do not make it look new.
/// </summary>
public static partial class Fingerprints
{
    public const string Key = "neolinter/v1";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Path relative to <paramref name="root"/> with '/' separators (a '../' path when outside the root).</summary>
    public static string RelativePath(string root, string filePath) =>
        Path.GetRelativePath(root, Path.GetFullPath(filePath)).Replace('\\', '/');

    /// <summary>One fingerprint per finding, in the order of <see cref="AnalysisReport.Findings"/>.</summary>
    public static IReadOnlyList<string> Compute(AnalysisReport report)
    {
        var root = Path.GetFullPath(report.RootDirectory);
        var lineCache = new Dictionary<string, string[]>();
        var seen = new Dictionary<string, int>();
        var result = new List<string>(report.Findings.Count);

        foreach (var finding in report.Findings)
        {
            var path = Path.GetFullPath(finding.Location.FilePath);
            if (!lineCache.TryGetValue(path, out var lines))
            {
                lines = File.Exists(path) ? File.ReadAllLines(path) : [];
                lineCache[path] = lines;
            }

            var index = finding.Location.StartLine - 1;
            var text = index >= 0 && index < lines.Length ? Whitespace().Replace(lines[index], " ").Trim() : string.Empty;
            var identity = string.Join('\n', finding.RuleId, RelativePath(root, path), text);
            seen[identity] = seen.GetValueOrDefault(identity) + 1;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{identity}\n{seen[identity] - 1}"));
            result.Add(Convert.ToHexStringLower(hash));
        }

        return result;
    }
}
