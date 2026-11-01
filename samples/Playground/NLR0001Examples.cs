// NLR0001: invert 'if' to reduce nesting.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Playground;

public class NLR0001Examples
{
    // --- Reported --------------------------------------------------------

    public void ReportedVoidMethod(string? name)
    {
        if (name != null)
        {
            Console.WriteLine("Hello");
            Console.WriteLine(name);
            Console.WriteLine("Bye");
        }
    }

    public void ReportedLoop(int[] values)
    {
        foreach (var value in values)
        {
            if (value > 0)
            {
                Console.WriteLine(value);
                Console.WriteLine(value * 2);
                Console.WriteLine(value * 3);
            }
        }
    }

    public int ReportedNonVoidWithSimpleReturn(List<int> items)
    {
        var total = 0;
        if (items.Count > 0)
        {
            total += items[0];
            total += items[^1];
            total *= 2;
        }
        return total;
    }

    public void ReportedElseOnlyThrows(bool ready)
    {
        if (ready)
        {
            Console.WriteLine("Starting");
        }
        else
        {
            throw new InvalidOperationException("Not ready.");
        }

        Console.WriteLine("Started");
    }

    public async Task ReportedAsync(object? state)
    {
        // Comments around the body are kept by the fix.
        if (state is null)
        {
            await Task.Delay(1);
            Console.WriteLine("No state"); // trailing comment
            await Task.Delay(1);
        }
    }

    public void ReportedDoubleComparison(double ratio)
    {
        // The fix writes !(ratio < 0.5): ratio >= 0.5 would be wrong for NaN.
        if (ratio < 0.5)
        {
            Console.WriteLine(ratio);
            Console.WriteLine(ratio * 2);
            Console.WriteLine(ratio * 3);
        }
    }

    public IEnumerable<int> ReportedIteratorLeavesWithYieldBreak(bool flag)
    {
        yield return 0;
        if (flag)
        {
            yield return 1;
            yield return 2;
            yield return 3;
        }
    }

    public IEnumerable<int> ReportedIteratorElseYieldBreak(bool flag)
    {
        if (flag)
        {
            yield return 1;
        }
        else
        {
            yield break;
        }

        yield return 2;
    }

    public void ReportedElseIfCascadeWithExitingElse(bool first, bool second)
    {
        // The final else only leaves: the fix writes if (!first && !second) { return; } and keeps the branches.
        if (first)
        {
            Console.WriteLine("first");
        }
        else if (second)
        {
            Console.WriteLine("second");
        }
        else
        {
            return;
        }

        Console.WriteLine("done");
    }

    public void ReportedNegatedConjunction(bool ready, bool enabled)
    {
        // With nlr0001.simplify_logical_operators = true the fix writes !ready || !enabled instead of !(ready && enabled).
        // With nlr0001.guard_braces = never the inserted return has no braces.
        if (ready && enabled)
        {
            Console.WriteLine(1);
            Console.WriteLine(2);
            Console.WriteLine(3);
        }
    }

    // --- Not reported ----------------------------------------------------

    public void NotReportedBelowThreshold(bool flag)
    {
        if (flag)
        {
            Console.WriteLine(1);
            Console.WriteLine(2);
        }
    }

    public void NotReportedCodeAfterIf(bool flag)
    {
        if (flag)
        {
            Console.WriteLine(1);
            Console.WriteLine(2);
            Console.WriteLine(3);
        }

        Console.WriteLine("always");
    }

    public void NotReportedElseIfCascade(int x)
    {
        if (x == 0)
        {
            Console.WriteLine(1);
            Console.WriteLine(2);
            Console.WriteLine(3);
        }
        else if (x == 1)
        {
            return;
        }
    }

    public int NotReportedReturnWithCall(bool flag)
    {
        if (flag)
        {
            Console.WriteLine(1);
            Console.WriteLine(2);
            Console.WriteLine(3);
        }
        return ComputeTotal();
    }

    private static int ComputeTotal() => 0;

    public IEnumerable<int> NotReportedIteratorCodeAfterIf(bool flag)
    {
        if (flag)
        {
            yield return 1;
            yield return 2;
            yield return 3;
        }

        yield return 4;
    }

    public void NotReportedElseIfCascadeWithCall(bool first)
    {
        // The condition of the second branch has a call: the cascade would evaluate it twice.
        if (first)
        {
            Console.WriteLine(1);
        }
        else if (IsReady())
        {
            Console.WriteLine(2);
        }
        else
        {
            return;
        }

        Console.WriteLine(3);
    }

    public void NotReportedElseIfCascadeBranchOnlyExits(int x)
    {
        if (x == 0)
        {
            Console.WriteLine(1);
            Console.WriteLine(2);
            Console.WriteLine(3);
        }
        else if (x == 1)
        {
            return;
        }
        else
        {
            return;
        }
    }

    public void NotReportedElseIfCascadeFinalElseDoesMore(bool first, bool second)
    {
        if (first)
        {
            Console.WriteLine(1);
        }
        else if (second)
        {
            Console.WriteLine(2);
        }
        else
        {
            Console.WriteLine("neither");
            return;
        }

        Console.WriteLine(3);
    }

    private static bool IsReady() => true;
}
