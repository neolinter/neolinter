// NLR0010: use the condition instead of 'true' and 'false'.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0010Examples
{
    // --- Reported --------------------------------------------------------

    public bool ReportedTernary(int count) => count > 3 ? true : false;

    public bool ReportedNegatedTernary(bool flag) => flag ? false : true;

    public bool ReportedIfElse(bool flag)
    {
        if (flag)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool ReportedIfThenReturn(string text)
    {
        if (text.Length > 0)
            return false;

        return true;
    }

    // --- Not reported ----------------------------------------------------

    public bool NotReportedDoesMore(bool flag)
    {
        if (flag)
        {
            Log();
            return true;
        }

        return false;
    }

    public bool NotReportedDynamic(dynamic value) => value ? true : false;

    public bool NotReportedComment(bool flag)
    {
        if (flag)
            return true; // deliberate

        return false;
    }

    private static void Log()
    {
    }
}
