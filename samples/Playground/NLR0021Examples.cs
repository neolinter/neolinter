// NLR0021: use string interpolation.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0021Examples
{
    private const string Prefix = "id-";

    // --- Reported --------------------------------------------------------

    public string ReportedGreeting(string name)
    {
        return "Hello, " + name + "!";
    }

    public string ReportedNumber(int count)
    {
        return "Items: " + count;
    }

    public string ReportedConditional(int count)
    {
        return "Items: " + (count > 1 ? "many" : "one");
    }

    // --- Not reported ----------------------------------------------------

    public string NotReportedTwoValues(string first, string last)
    {
        return first + last;
    }

    // A constant stays a constant.
    public const string NotReportedConstant = Prefix + "1";

    public string NotReportedVerbatim(string name)
    {
        return @"C:\temp\" + name;
    }
}
