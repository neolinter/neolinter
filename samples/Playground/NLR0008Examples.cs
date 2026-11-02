// NLR0008: merge nested 'if' statements.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0008Examples
{
    // --- Reported --------------------------------------------------------

    public void ReportedBraces(bool ready, bool visible)
    {
        if (ready)
        {
            if (visible)
            {
                Show();
            }
        }
    }

    public void ReportedWithoutBraces(bool ready, bool visible)
    {
        if (ready)
            if (visible)
                Show();
    }

    public void ReportedOnOneLine(bool ready, bool visible)
    {
        if (ready) { if (visible) Show(); }
    }

    public void ReportedChain(bool a, bool b, bool c)
    {
        if (a)
        {
            if (b)
            {
                if (c)
                {
                    Show();
                }
            }
        }
    }

    // The fix writes (ready || forced) && visible.
    public void ReportedParenthesesNeeded(bool ready, bool forced, bool visible)
    {
        if (ready || forced)
        {
            if (visible)
            {
                Show();
            }
        }
    }

    // The pattern variable stays in scope for the body.
    public void ReportedPatternVariable(object? value)
    {
        if (value is string text)
        {
            if (text.Length > 0)
            {
                Show();
            }
        }
    }

    // --- Not reported ----------------------------------------------------

    public void NotReportedOuterElse(bool ready, bool visible)
    {
        if (ready)
        {
            if (visible)
            {
                Show();
            }
        }
        else
        {
            Hide();
        }
    }

    public void NotReportedInnerElse(bool ready, bool visible)
    {
        if (ready)
        {
            if (visible)
            {
                Show();
            }
            else
            {
                Hide();
            }
        }
    }

    public void NotReportedAnotherStatement(bool ready, bool visible)
    {
        if (ready)
        {
            Hide();
            if (visible)
            {
                Show();
            }
        }
    }

    public void NotReportedComment(bool ready, bool visible)
    {
        if (ready)
        {
            // only when it is also visible
            if (visible)
            {
                Show();
            }
        }
    }

    private static void Show() { }

    private static void Hide() { }
}
