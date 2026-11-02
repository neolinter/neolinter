// NLR0018: move the 'break' guard into 'TakeWhile'.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0018Examples
{
    // --- Reported --------------------------------------------------------

    public int ReportedBreakGuard(int[] numbers)
    {
        var total = 0;
        foreach (var number in numbers)
        {
            if (number < 0) break;
            total += number;
        }

        return total;
    }

    // --- Not reported ----------------------------------------------------

    // The order of a 'continue' guard and a 'break' guard matters: no single call replaces both.
    public int NotReportedContinueThenBreak(int[] numbers)
    {
        var total = 0;
        foreach (var number in numbers)
        {
            if (number == 0) continue;
            if (number < 0) break;
            total += number;
        }

        return total;
    }

    public int NotReportedConditionCallsAMethod(List<string> names)
    {
        var total = 0;
        foreach (var name in names)
        {
            if (IsStop(name)) break;
            total += name.Length;
        }

        return total;
    }

    private static bool IsStop(string name) => name.Length == 0;
}
