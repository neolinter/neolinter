// NLR0007: avoid nested conditional expressions.
// Methods named Reported* get the diagnostic (and a code fix where noted); NotReported* must stay silent.
namespace Playground;

public class NLR0007Examples
{
    private string _label = "";

    // --- Reported, with a code fix ---------------------------------------------------------------

    public string ReportedReturnChain(int n)
    {
        return n < 0 ? "negative" : n == 0 ? "zero" : "positive";
    }

    public void ReportedAssignmentChain(int n)
    {
        _label = n < 0 ? "negative" : n == 0 ? "zero" : "positive";
    }

    public string ReportedDeclarationChain(int n)
    {
        string label = n < 0 ? "negative" : n == 0 ? "zero" : "positive";
        return label;
    }

    // --- Reported, no code fix (the diagnostic still helps) -----------------------------------------

    public string ReportedExpressionBody(int n) => n < 0 ? "negative" : n == 0 ? "zero" : "positive";

    public int ReportedNestedInTheTrueBranch(bool a, bool b) => a ? b ? 1 : 2 : 3;

    // The fix is skipped: the branches are converted to 'int?' together, not one by one.
    public int? ReportedTargetTyped(bool a, bool b) => a ? 1 : b ? 2 : null;

    // --- Not reported ----------------------------------------------------------------------------

    public string NotReportedSingleConditional(int n) => n < 0 ? "negative" : "other";

    public int NotReportedTwoSeparateConditionals(bool a, bool b) => (a ? 1 : 2) + (b ? 3 : 4);

    public string NotReportedAlreadyAChain(int n)
    {
        if (n < 0)
        {
            return "negative";
        }
        else if (n == 0)
        {
            return "zero";
        }

        return "positive";
    }
}
