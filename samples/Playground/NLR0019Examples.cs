// NLR0019: use a switch expression.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0019Examples
{
    private int _discount;

    // --- Reported --------------------------------------------------------

    public string ReportedReturn(int day)
    {
        switch (day)
        {
            case 6:
            case 7:
                return "weekend";
            default:
                return "weekday";
        }
    }

    public void ReportedAssignment(string tier)
    {
        switch (tier)
        {
            case "gold":
                _discount = 20;
                break;
            case "silver":
                _discount = 10;
                break;
            default:
                _discount = 0;
                break;
        }
    }

    public int ReportedThrowingDefault(object value)
    {
        switch (value)
        {
            case int number when number > 0:
                return number;
            case string text:
                return text.Length;
            default:
                throw new ArgumentException("Unsupported value", nameof(value));
        }
    }

    // --- Not reported ----------------------------------------------------

    // Without 'default' a switch expression would throw where the statement does nothing.
    public int NotReportedNoDefault(int day)
    {
        switch (day)
        {
            case 6: return 1;
            case 7: return 2;
        }

        return 0;
    }

    // A section does more than return a value.
    public int NotReportedSeveralStatements(int day)
    {
        switch (day)
        {
            case 6:
                Console.WriteLine("saturday");
                return 1;
            default:
                return 0;
        }
    }

    // A comment would be lost.
    public int NotReportedComment(int day)
    {
        switch (day)
        {
            case 6: return 1; // saturday
            default: return 0;
        }
    }

    // The sections assign different variables.
    public int NotReportedDifferentTargets(int day)
    {
        int a = 0, b = 0;
        switch (day)
        {
            case 6: a = 1; break;
            default: b = 1; break;
        }

        return a + b;
    }
}
