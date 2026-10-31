namespace Neolinter.Cli.Core;

/// <summary>Process exit codes of the neolinter command line. Part of the public contract: do not renumber.</summary>
public static class ExitCodes
{
    /// <summary>The analysis ran and found nothing.</summary>
    public const int Ok = 0;

    /// <summary>The analysis ran and reported at least one finding.</summary>
    public const int Findings = 1;

    /// <summary>The analysis could not run (bad arguments, invalid configuration, load failure, not implemented).</summary>
    public const int Error = 2;
}
