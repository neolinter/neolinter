// NLR0035: Collection modified during foreach.
// Types named Reported* hold the diagnostic (report only, no code fix); NotReported* must stay silent.
namespace Playground;

// --- Reported ------------------------------------------------------------

public class ReportedRemoveInLoop
{
    public void RemoveEven(List<int> numbers)
    {
        foreach (var n in numbers)
        {
            if (n % 2 == 0)
                numbers.Remove(n);
        }
    }
}

public class ReportedAddInLoop
{
    public void Grow(HashSet<int> ids)
    {
        foreach (var id in ids)
        {
            ids.Add(id + 1);
        }
    }
}

// --- Not reported --------------------------------------------------------

public class NotReportedIterateCopy
{
    public void RemoveEven(List<int> numbers)
    {
        foreach (var n in numbers.ToList())
        {
            if (n % 2 == 0)
                numbers.Remove(n);
        }
    }
}

public class NotReportedBreakAfterRemove
{
    public void RemoveFirstEven(List<int> numbers)
    {
        foreach (var n in numbers)
        {
            if (n % 2 == 0)
            {
                numbers.Remove(n);
                break;
            }
        }
    }
}

public class NotReportedOtherCollection
{
    public void Copy(List<int> source, List<int> target)
    {
        foreach (var n in source)
        {
            target.Add(n);
        }
    }
}
