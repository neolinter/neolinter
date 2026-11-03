// NLR0032: multiple enumeration of an IEnumerable.
// Methods named Reported* hold the diagnostic (with a code fix that adds ToList() for a local); NotReported* must stay silent.
namespace Playground;

public class NLR0032Examples
{
    private static IEnumerable<int> Numbers() => new[] { 1, 2, 3 };

    // --- Reported --------------------------------------------------------

    public void ReportedAnyThenForeach(IEnumerable<string> names)
    {
        if (names.Any())
        {
            foreach (var name in names)
            {
                Console.WriteLine(name);
            }
        }
    }

    public int ReportedLocalQuery(IEnumerable<int> values)
    {
        var positive = values.Where(v => v > 0);
        var count = positive.Count();
        return count + positive.First();
    }

    public void ReportedInLoop(IEnumerable<int> values, List<int> others)
    {
        foreach (var other in others)
        {
            Console.WriteLine(values.Contains(other));
        }
    }

    public void ReportedQueryable(IQueryable<int> query)
    {
        var total = query.Count();
        var all = query.ToList();
        Console.WriteLine(total + all.Count);
    }

    // --- Not reported ----------------------------------------------------

    public int NotReportedSingleEnumeration(IEnumerable<int> values) => values.Count();

    public int NotReportedDifferentBranches(IEnumerable<int> values, bool first)
    {
        if (first)
        {
            return values.First();
        }

        return values.Last();
    }

    public int NotReportedMaterialized(IEnumerable<int> values)
    {
        var list = values.ToList();
        return list.Count + list.Sum();
    }

    public int NotReportedList(List<int> values) => values.Count() + values.Sum();

    public int NotReportedReassigned(IEnumerable<int> values)
    {
        var count = values.Count();
        values = values.ToList();
        return count + values.Sum();
    }

    public int NotReportedDeclaredInLoop(List<int> others)
    {
        var total = 0;
        foreach (var other in others)
        {
            var query = Numbers().Where(n => n > other);
            total += query.Count();
        }

        return total;
    }
}
