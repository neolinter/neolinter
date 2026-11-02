// NLR0011: use 'Any()' instead of comparing 'Count()' with zero.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0011Examples
{
    // --- Reported --------------------------------------------------------

    public bool ReportedHasItems(IEnumerable<int> numbers) => numbers.Count() > 0;

    public bool ReportedIsEmpty(IEnumerable<string> names) => names.Count() == 0;

    public bool ReportedMirrored(IEnumerable<string> names) => 0 < names.Count();

    public bool ReportedWithPredicate(IEnumerable<int> numbers) => numbers.Count(n => n > 10) != 0;

    public bool ReportedQueryable(IQueryable<int> numbers) => numbers.Count() >= 1;

    // --- Not reported ----------------------------------------------------

    public bool NotReportedCountProperty(List<int> numbers) => numbers.Count > 0;

    public bool NotReportedLength(int[] numbers) => numbers.Length > 0;

    public bool NotReportedOtherNumber(IEnumerable<int> numbers) => numbers.Count() > 1;

    public bool NotReportedOwnCount(Counter counter) => counter.Count() > 0;

    public bool NotReportedComment(IEnumerable<int> numbers) => numbers.Count() > /* at least one */ 0;

    public sealed class Counter
    {
        public int Count() => 0;
    }
}
