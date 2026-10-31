using System.Text.Json;

namespace Neolinter.Cli.Core;

/// <summary>A previous SARIF log: findings whose fingerprint appears in it are not reported again.</summary>
public sealed class Baseline
{
    private readonly HashSet<string> _fingerprints;

    private Baseline(HashSet<string> fingerprints) => _fingerprints = fingerprints;

    public static Baseline Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var fingerprints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var run in document.RootElement.GetProperty("runs").EnumerateArray())
            {
                if (!run.TryGetProperty("results", out var results))
                {
                    continue;
                }

                foreach (var result in results.EnumerateArray())
                {
                    if (result.TryGetProperty("partialFingerprints", out var partial)
                        && partial.TryGetProperty(Fingerprints.Key, out var value)
                        && value.GetString() is { } fingerprint)
                    {
                        fingerprints.Add(fingerprint);
                    }
                }
            }

            return new Baseline(fingerprints);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ConfigException($"Baseline '{path}' is not a valid SARIF log: {exception.Message}");
        }
    }

    /// <summary>The findings that are not in the baseline.</summary>
    public IReadOnlyList<Finding> Filter(AnalysisReport report)
    {
        var fingerprints = Fingerprints.Compute(report);
        return report.Findings.Where((_, index) => !_fingerprints.Contains(fingerprints[index])).ToList();
    }
}
