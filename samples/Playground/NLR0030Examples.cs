// NLR0030: ignored return value.
// Methods named Reported* hold the diagnostic (with a code fix that assigns the result back when it is safe); NotReported* must stay silent.
using System.Collections.Immutable;
using System.Diagnostics.Contracts;

namespace Playground;

public class NLR0030Examples
{
    private string title = " title ";

    // --- Reported --------------------------------------------------------

    public void ReportedReplace(string text)
    {
        text.Replace("a", "b");
    }

    public void ReportedTrimField()
    {
        title.Trim();
    }

    public void ReportedDate(DateTime date)
    {
        date.AddDays(1);
    }

    public void ReportedImmutable(ImmutableArray<int> items)
    {
        items.Add(1);
    }

    public void ReportedLinq(List<int> items)
    {
        items.Select(i => i * 2);
    }

    public void ReportedPureAttribute()
    {
        Twice(2);
    }

    // --- Not reported ----------------------------------------------------

    public string NotReportedUsed(string text)
    {
        return text.Trim();
    }

    public void NotReportedDiscard(string text)
    {
        _ = text.Trim();
    }

    public void NotReportedMutation(List<int> items)
    {
        items.Add(1);
        items.Sort();
    }

    public void NotReportedUserMethod()
    {
        Log("done");
    }

    [Pure]
    private static int Twice(int value) => value * 2;

    private static string Log(string message) => message;
}
