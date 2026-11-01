// NLC0001: duplicated code, reported by "neolinter analyze duplicates samples/Playground".
// One group is expected: ReportedFirstCopy and ReportedSecondCopy repeat the same 8 statements
// (names and literals differ, which does not matter). NotReported* methods must stay silent.
using System;

namespace Playground;

public static class DuplicateExamples
{
    // --- Reported --------------------------------------------------------

    internal static int ReportedFirstCopy(int[] values)
    {
        var total = 0;
        var count = 0;
        var largest = 0;
        foreach (var value in values)
        {
            if (value > 0)
            {
                total += value;
                count++;
                largest = Math.Max(largest, value);
            }
        }

        var average = count == 0 ? 0 : total / count;
        Console.WriteLine($"average: {average}");
        Console.WriteLine($"largest: {largest}");
        return average;
    }

    internal static int ReportedSecondCopy(int[] readings)
    {
        var sum = 0;
        var seen = 0;
        var peak = 0;
        foreach (var reading in readings)
        {
            if (reading > 10)
            {
                sum += reading;
                seen++;
                peak = Math.Max(peak, reading);
            }
        }

        var mean = seen == 0 ? 0 : sum / seen;
        Console.WriteLine($"mean: {mean}");
        Console.WriteLine($"peak: {peak}");
        return mean;
    }

    // --- Not reported ----------------------------------------------------

    // Only three statements in common with the copies above: below the minimum of 8.
    internal static int NotReportedShort(int[] values)
    {
        var total = 0;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }

    // An extra statement in the middle breaks every run of 8 identical statements.
    internal static int NotReportedAlmostACopy(int[] values)
    {
        var total = 0;
        var count = 0;
        var largest = 0;
        foreach (var value in values)
        {
            if (value > 0)
            {
                total += value;
                count++;
                largest = Math.Max(largest, value);
            }
        }

        Console.WriteLine("computing");
        var average = count == 0 ? 0 : total / count;
        Console.WriteLine($"average: {average}");
        Console.WriteLine($"largest: {largest}");
        return average;
    }
}
