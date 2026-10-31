namespace Neolinter.Cli.Core;

/// <summary>Rules reported by the command line. Ids are part of the public contract; each has a /rules/{id}.yaml file.</summary>
public static class CliRules
{
    public static readonly RuleDescriptor UnusedMember = new(
        "NLD0001",
        "UnusedMember",
        "This member is not referenced anywhere in the solution.",
        FindingLevel.Warning);

    public static readonly RuleDescriptor DuplicatedCode = new(
        "NLC0001",
        "DuplicatedCode",
        "This sequence of statements is repeated elsewhere in the solution.",
        FindingLevel.Warning);

    public static IReadOnlyList<RuleDescriptor> All { get; } = [UnusedMember, DuplicatedCode];
}
