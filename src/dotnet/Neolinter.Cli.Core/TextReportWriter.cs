using System.Globalization;
using System.Text;

namespace Neolinter.Cli.Core;

/// <summary>Human-readable report: <c>path(line,column): level RULEID: message</c>, one result per line.</summary>
public static class TextReportWriter
{
    public static string Write(AnalysisReport report)
    {
        var root = Path.GetFullPath(report.RootDirectory);
        var builder = new StringBuilder();
        foreach (var finding in report.Findings)
        {
            var level = finding.Level.ToString().ToLowerInvariant();
            builder.Append(CultureInfo.InvariantCulture, $"{Format(finding.Location, root)}: {level} {finding.RuleId}: {finding.Message}\n");
            foreach (var related in finding.RelatedLocations)
            {
                builder.Append(CultureInfo.InvariantCulture, $"  related: {Format(related, root)}\n");
            }
        }

        return builder.ToString();
    }

    private static string Format(SourceLocation location, string root) =>
        $"{Fingerprints.RelativePath(root, location.FilePath)}({location.StartLine.ToString(CultureInfo.InvariantCulture)},{location.StartColumn.ToString(CultureInfo.InvariantCulture)})";
}
