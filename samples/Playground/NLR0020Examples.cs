// NLR0020: use a conditional expression.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0020Examples
{
    private string _label = "";

    // --- Reported --------------------------------------------------------

    public void ReportedAssignment(int count)
    {
        if (count == 1)
            _label = "item";
        else
            _label = "items";
    }

    public string ReportedReturn(bool isAdmin)
    {
        if (isAdmin)
        {
            return "admin";
        }
        else
        {
            return "guest";
        }
    }

    public string ReportedReturnWithoutElse(bool isAdmin)
    {
        if (isAdmin)
            return "admin";
        return "guest";
    }

    // --- Not reported ----------------------------------------------------

    // NLR0010 owns this shape.
    public bool NotReportedBooleanLiterals(bool value)
    {
        if (value)
            return true;
        return false;
    }

    // The result would be a nested conditional expression (NLR0007).
    public string NotReportedNested(int n, bool other)
    {
        if (n > 0)
            return other ? "a" : "b";
        return "c";
    }

    // The branches assign different variables.
    public int NotReportedDifferentTargets(bool value)
    {
        int a = 0, b = 0;
        if (value)
            a = 1;
        else
            b = 1;
        return a + b;
    }

    // A branch does more than assign.
    public void NotReportedSeveralStatements(bool value)
    {
        if (value)
        {
            _label = "yes";
            System.Console.WriteLine(_label);
        }
        else
        {
            _label = "no";
        }
    }

    // A comment would be lost.
    public string NotReportedComment(bool value)
    {
        if (value)
            return "yes"; // the usual case
        return "no";
    }

    // An 'else if' chain is left alone.
    public string NotReportedElseIf(int n)
    {
        if (n < 0)
            return "negative";
        else if (n == 0)
            return "zero";
        else
            return "positive";
    }

    // The conditional expression would be an 'int' converted afterwards, not a 'double' from the start.
    public double NotReportedOtherType(bool value)
    {
        double result;
        if (value)
            result = 1;
        else
            result = 2;
        return result;
    }
}
