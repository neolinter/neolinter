// NLC0002: unused member, reported by "neolinter analyze dead-code samples/Playground".
// Members named Reported* get a finding; NotReported* must stay silent.
// neolinter.json excludes NLR0001Examples.cs, whose methods exist only to show NLR0001.
using System;
using System.Runtime.CompilerServices;

namespace Playground;

public class DeadCodeExamples
{
    // --- Reported --------------------------------------------------------

    internal static void ReportedNeverCalled()
    {
    }

    private int reportedUnusedField;

    // --- Not reported ----------------------------------------------------

    internal static int NotReportedHelper() => 42;

    public int NotReportedUsedByProgram() => NotReportedHelper() + NotReportedRecursive(1);

    // A call from inside its own body does not count, but the call above does.
    private static int NotReportedRecursive(int depth) => depth == 0 ? 0 : NotReportedRecursive(depth - 1);

    public override string ToString() => nameof(NotReportedOnlyInNameof);

    private static void NotReportedOnlyInNameof()
    {
    }

    [ModuleInitializer]
    internal static void NotReportedModuleInitializer()
    {
    }
}

internal sealed class NotReportedDisposable : IDisposable
{
    // Interface implementations are used through the interface.
    public void Dispose()
    {
    }
}

public static class Program
{
    public static void Main()
    {
        using var disposable = new NotReportedDisposable();
        Console.WriteLine(new DeadCodeExamples().NotReportedUsedByProgram());

        // Keeps the duplicate examples from also showing up as dead code.
        int[] values = [1, 2, 3];
        Console.WriteLine(DuplicateExamples.ReportedFirstCopy(values) + DuplicateExamples.ReportedSecondCopy(values));
        Console.WriteLine(DuplicateExamples.NotReportedShort(values) + DuplicateExamples.NotReportedAlmostACopy(values));
    }
}
