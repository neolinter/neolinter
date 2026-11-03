// NLR0029: self-assignment.
// Methods named Reported* hold the diagnostic (with a code fix that removes the statement); NotReported* must stay silent.
namespace Playground;

public class NLR0029Examples
{
    private int count;

    public int Auto { get; set; }

    public int Body { get => count; set => count = value; }

    private NLR0029Examples? next;

    // --- Reported --------------------------------------------------------

    public int ReportedParameter(int value)
    {
        value = value;
        return value;
    }

    public void ReportedThisField()
    {
        this.count = this.count;
    }

    public void ReportedAutoProperty()
    {
        Auto = this.Auto;
    }

    public void ReportedSameReceiver(NLR0029Examples other)
    {
        other.count = other.count;
    }

    // --- Not reported ----------------------------------------------------

    public void NotReportedConstructorStyle(int count)
    {
        this.count = count;
    }

    public void NotReportedDifferentReceivers(NLR0029Examples a, NLR0029Examples b)
    {
        a.count = b.count;
    }

    public void NotReportedCompound(int value)
    {
        count += count;
        count |= value;
    }

    public void NotReportedPropertyWithBody()
    {
        Body = Body;
    }

    public void NotReportedCallInReceiver()
    {
        Next().count = Next().count;
    }

    public void NotReportedElement(int[] items, int i)
    {
        items[i] = items[i];
    }

    private NLR0029Examples Next() => next ??= new NLR0029Examples();
}
