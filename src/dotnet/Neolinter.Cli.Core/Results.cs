namespace Neolinter.Cli.Core;

public enum FindingLevel
{
    Note,
    Warning,
    Error,
}

/// <summary>A span in a source file. Lines and columns are 1-based, columns count UTF-16 code units.</summary>
public sealed record SourceLocation(string FilePath, int StartLine, int StartColumn, int EndLine, int EndColumn);

/// <summary>One result of a solution-wide analysis.</summary>
/// <param name="RelatedLocations">Other places involved, e.g. the other copies of a duplicated block.</param>
public sealed record Finding(string RuleId, string Message, FindingLevel Level, SourceLocation Location, IReadOnlyList<SourceLocation> RelatedLocations)
{
    public Finding(string ruleId, string message, FindingLevel level, SourceLocation location)
        : this(ruleId, message, level, location, [])
    {
    }
}

public sealed record RuleDescriptor(string Id, string Name, string ShortDescription, FindingLevel DefaultLevel)
{
    public string HelpUri => $"https://github.com/neolinter/neolinter/blob/main/rules/{Id}.yaml";
}

/// <summary>Everything needed to write one SARIF run.</summary>
/// <param name="RootDirectory">Files under this directory are written relative to it (uriBaseId SRCROOT).</param>
public sealed record AnalysisReport(string ToolVersion, string RootDirectory, IReadOnlyList<RuleDescriptor> Rules, IReadOnlyList<Finding> Findings);
