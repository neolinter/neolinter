// NLR0016: use a type pattern instead of a cast.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0016Examples
{
    private object? _value;

    // --- Reported --------------------------------------------------------

    public int ReportedAsThenNullCheck(object o)
    {
        var text = o as string;
        if (text != null)
        {
            return text.Length;
        }

        return 0;
    }

    public int ReportedAsThenGuard(object o)
    {
        var text = o as string;
        if (text == null)
        {
            return 0;
        }

        return text.Length;
    }

    public int ReportedIsThenCast(object o)
    {
        if (o is string)
        {
            return ((string)o).Length;
        }

        return 0;
    }

    public int ReportedIsThenCastOnField()
    {
        if (_value is string)
        {
            return ((string)_value).Length;
        }

        return 0;
    }

    // --- Not reported ----------------------------------------------------

    public int NotReportedUsedAfter(object o)
    {
        var text = o as string;
        if (text != null)
        {
            Console.WriteLine(text);
        }

        return text is null ? 0 : text.Length;
    }

    public int NotReportedValueChanges(object o, object other)
    {
        if (o is string)
        {
            o = other;
            return ((string)o).Length;
        }

        return 0;
    }

    public int NotReportedCallBeforeCastOnField()
    {
        if (_value is string)
        {
            Console.WriteLine("checking");
            return ((string)_value).Length;
        }

        return 0;
    }
}
