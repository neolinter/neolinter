// NLR0017: move the 'continue' guard into 'Where'.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0017Examples
{
    private int _limit;

    // --- Reported --------------------------------------------------------

    public int ReportedNegatedGuard(List<string> names)
    {
        var total = 0;
        foreach (var name in names)
        {
            if (!name.StartsWith("a")) continue;
            total += name.Length;
        }

        return total;
    }

    public int ReportedSeveralGuards(int[] numbers)
    {
        var total = 0;
        foreach (var number in numbers)
        {
            if (number < 0)
            {
                continue;
            }

            if (number > _limit) continue;
            total += number;
        }

        return total;
    }

    // --- Not reported ----------------------------------------------------

    public int NotReportedBreak(int[] numbers)
    {
        var total = 0;
        foreach (var number in numbers)
        {
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
            if (IsSkipped(name)) continue;
            total += name.Length;
        }

        return total;
    }

    public int NotReportedBodyChangesTheCondition(int[] numbers)
    {
        var total = 0;
        foreach (var number in numbers)
        {
            if (total > 100) continue;
            total += number;
        }

        return total;
    }

    public int NotReportedQueryable(IQueryable<int> numbers)
    {
        var total = 0;
        foreach (var number in numbers)
        {
            if (number < 0) continue;
            total += number;
        }

        return total;
    }

    private static bool IsSkipped(string name) => name.Length == 0;
}
