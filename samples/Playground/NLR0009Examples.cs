// NLR0009: remove 'else' after a branch that exits.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0009Examples
{
    private int total;

    // --- Reported --------------------------------------------------------

    public void ReportedBlock(bool done)
    {
        if (done)
        {
            return;
        }
        else
        {
            Work();
            Work();
        }
    }

    public void ReportedBraceOnTheSameLine(bool done)
    {
        if (done) {
            return;
        } else {
            Work();
        }
        Work();
    }

    public int ReportedBothReturn(bool valid)
    {
        if (valid)
        {
            return 1;
        }
        else
        {
            return 2;
        }
    }

    public void ReportedThrow(string? name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));
        else
            Work();
    }

    public void ReportedContinue(int[] values)
    {
        foreach (var value in values)
        {
            if (value < 0)
            {
                continue;
            }
            else
            {
                total += value;
            }
        }
    }

    public IEnumerable<int> ReportedYieldBreak(bool empty)
    {
        if (empty)
        {
            yield break;
        }
        else
        {
            yield return 1;
        }
    }

    // The comments follow the statements.
    public void ReportedComments(bool done)
    {
        if (done)
        {
            return;
        }
        else
        {
            // the normal path
            Work(); // once
        }
    }

    // --- Not reported ----------------------------------------------------

    // The chain is left alone.
    public void NotReportedElseIf(bool a, bool b)
    {
        if (a)
            return;
        else if (b)
            Work();
        else
            Work();
    }

    public void NotReportedBranchDoesNotExit(bool a)
    {
        if (a)
        {
            Work();
        }
        else
        {
            Work();
        }
    }

    // The else would stop being attached to the inner 'if'.
    public void NotReportedDanglingElse(bool a, bool b)
    {
        if (a)
            if (b)
                return;
            else
                Work();
    }

    // The comment on the else would be lost.
    public void NotReportedComment(bool done)
    {
        if (done)
        {
            return;
        }
        // otherwise
        else
        {
            Work();
        }
    }

    // 'total' would become a local of the whole method.
    public void NotReportedNameClash(bool done)
    {
        if (done)
        {
            return;
        }
        else
        {
            var total = 1;
            this.total += total;
        }
        this.total = 2;
    }

    // The 'using' would be disposed after Work(), not before it.
    public void NotReportedUsing(bool done)
    {
        if (done)
        {
            return;
        }
        else
        {
            using var reader = new StringReader("");
            reader.ReadLine();
        }
        Work();
    }

    private static void Work() { }
}
