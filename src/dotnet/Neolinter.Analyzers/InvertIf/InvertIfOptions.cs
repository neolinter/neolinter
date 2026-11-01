using Microsoft.CodeAnalysis.Diagnostics;

namespace Neolinter.Analyzers.InvertIf;

/// <summary>Options that only change how the code fix writes its result; the diagnostics do not depend on them.</summary>
internal static class InvertIfOptions
{
    public const string SimplifyLogicalOperatorsOption = "nlr0001.simplify_logical_operators";
    public const string GuardBracesOption = "nlr0001.guard_braces";

    // Invalid values fall back to the default, like the other options.
    public static bool GetSimplifyLogicalOperators(AnalyzerConfigOptions options) =>
        options.TryGetValue(SimplifyLogicalOperatorsOption, out var value)
        && string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>true</c> for <c>guard_braces = always</c> (default), <c>false</c> for <c>never</c>.</summary>
    public static bool GetGuardBraces(AnalyzerConfigOptions options) =>
        !(options.TryGetValue(GuardBracesOption, out var value)
          && string.Equals(value.Trim(), "never", StringComparison.OrdinalIgnoreCase));
}
