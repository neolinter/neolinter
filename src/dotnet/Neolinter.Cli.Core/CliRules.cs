namespace Neolinter.Cli.Core;

/// <summary>Rules reported by the command line. Ids are part of the public contract; each has a /rules/{id}.yaml file.</summary>
public static class CliRules
{
    public static readonly RuleDescriptor UnusedMember = new(
        "NLC0002",
        "UnusedMember",
        "This type or member is not referenced anywhere in the solution.",
        FindingLevel.Warning);

    public static readonly RuleDescriptor DuplicatedCode = new(
        "NLC0003",
        "DuplicatedCode",
        "This sequence of statements is repeated elsewhere in the solution.",
        FindingLevel.Warning);

    public static IReadOnlyList<RuleDescriptor> All { get; } = [UnusedMember, DuplicatedCode];
}
