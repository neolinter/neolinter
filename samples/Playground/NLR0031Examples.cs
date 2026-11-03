// NLR0031: lock on a publicly visible object.
// Methods named Reported* hold the diagnostic (report only, no code fix); NotReported* must stay silent.
namespace Playground;

public class NLR0031Examples
{
    private readonly object _gate = new();
    private int _count;

    // --- Reported --------------------------------------------------------

    public void ReportedLockThis()
    {
        lock (this)
        {
            _count++;
        }
    }

    public void ReportedLockString()
    {
        lock ("counter")
        {
            _count++;
        }
    }

    public void ReportedLockTypeof()
    {
        lock (typeof(NLR0031Examples))
        {
            _count++;
        }
    }

    public void ReportedLockBoxedValue()
    {
        lock ((object)_count)
        {
            _count++;
        }
    }

    // --- Not reported ----------------------------------------------------

    public void NotReportedPrivateObject()
    {
        lock (_gate)
        {
            _count++;
        }
    }
}

public sealed class NLR0031Sealed
{
    private int _count;

    public void NotReportedLockThisInSealedClass()
    {
        lock (this)
        {
            _count++;
        }
    }
}
