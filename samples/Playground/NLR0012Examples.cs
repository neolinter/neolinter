// NLR0012: fold 'Where' into the following LINQ operator.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0012Examples
{
    // --- Reported --------------------------------------------------------

    public bool ReportedAny(IEnumerable<int> numbers) => numbers.Where(n => n > 10).Any();

    public int ReportedFirst(IEnumerable<int> numbers) => numbers.Where(n => n > 10).First();

    public int? ReportedLastOrDefault(IQueryable<int> numbers) => numbers.Where(n => n > 10).LastOrDefault();

    public int ReportedCount(IEnumerable<string> names) => names.Where(IsLong).Count();

    // --- Not reported ----------------------------------------------------

    public bool NotReportedIndexOverload(IEnumerable<int> numbers) => numbers.Where((n, i) => i > n).Any();

    public int NotReportedOwnPredicate(IEnumerable<int> numbers) => numbers.Where(n => n > 10).First(n => n < 100);

    // NLR0011 takes this one; its fix leaves Where(p).Any(), which NLR0012 then takes.
    public bool NotReportedCountAgainstZero(IEnumerable<int> numbers) => numbers.Where(n => n > 10).Count() > 0;

    public int NotReportedOwnFirst(Own own) => own.Where(n => n > 10).First();

    private static bool IsLong(string name) => name.Length > 10;

    public sealed class Own : List<int>
    {
        public int First() => 0;
    }
}
