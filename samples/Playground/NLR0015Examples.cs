// NLR0015: use 'TryGetValue' instead of 'ContainsKey' and the indexer.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
using System.Linq.Expressions;

namespace Playground;

public class NLR0015Examples
{
    private readonly Dictionary<string, int> _counts = new();

    // --- Reported --------------------------------------------------------

    public int ReportedIf(Dictionary<string, int> map, string key)
    {
        if (map.ContainsKey(key))
        {
            return map[key];
        }

        return 0;
    }

    public void ReportedNegatedElse(string key)
    {
        if (!_counts.ContainsKey(key))
        {
            Console.WriteLine("missing");
        }
        else
        {
            Console.WriteLine(_counts[key]);
        }
    }

    public bool ReportedAnd(Dictionary<string, int> map, string key) => map.ContainsKey(key) && map[key] > 0;

    // --- Not reported ----------------------------------------------------

    public int NotReportedAssignment(Dictionary<string, int> map, string key)
    {
        if (map.ContainsKey(key))
        {
            map[key] += 1;
        }

        return 0;
    }

    public int NotReportedRemoved(Dictionary<string, int> map, string key)
    {
        if (map.ContainsKey(key))
        {
            map.Remove(key);
            return map[key];
        }

        return 0;
    }

    public int NotReportedOtherKey(Dictionary<string, int> map, string key, string other)
    {
        if (map.ContainsKey(key))
        {
            return map[other];
        }

        return 0;
    }

    public Expression<Func<string, bool>> NotReportedExpressionTree(Dictionary<string, int> map) => k => map.ContainsKey(k) && map[k] > 0;
}
