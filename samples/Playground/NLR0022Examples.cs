// NLR0022: use string interpolation instead of string.Format.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
using System.Globalization;

namespace Playground;

public class NLR0022Examples
{
    // --- Reported --------------------------------------------------------

    public string ReportedGreeting(string name)
    {
        return string.Format("Hello, {0}!", name);
    }

    public string ReportedAlignedPrice(string name, decimal price)
    {
        return string.Format("{0,-10}{1:F2}", name, price);
    }

    public string ReportedRepeated(string name)
    {
        return string.Format("{0} {0}", name);
    }

    // --- Not reported ----------------------------------------------------

    public string NotReportedCulture(decimal price)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:F2}", price);
    }

    public string NotReportedUnusedArgument(string name, int count)
    {
        return string.Format("{0}", name, count);
    }

    public string NotReportedRepeatedCall(string name)
    {
        return string.Format("{0}{0}", name.Trim());
    }
}
