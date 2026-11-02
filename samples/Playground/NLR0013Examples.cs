// NLR0013: use 'is null' instead of comparing with 'null'.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
using System.Linq.Expressions;

namespace Playground;

public class NLR0013Examples
{
    // --- Reported --------------------------------------------------------

    public bool ReportedEquals(string name) => name == null;

    public bool ReportedNullOnTheLeft(object value) => null == value;

    public bool ReportedNotEquals(string name) => name != null;

    public bool ReportedNullable(int? number) => number == null;

    public bool ReportedLinqToObjects(IEnumerable<string> names) => names.Any(n => n == null);

    // --- Not reported ----------------------------------------------------

    public bool NotReportedOwnEquality(Money money) => money == null;

    public bool NotReportedExpressionTree(IQueryable<string> names) => names.Any(n => n != null);

    public Expression<Func<string, bool>> NotReportedExpressionLambda() => n => n == null;

    public bool NotReportedAlreadyAPattern(string name) => name is null;

    public bool NotReportedComment(string name) => name /* deliberate */ == null;

    public sealed class Money
    {
        public static bool operator ==(Money? a, Money? b) => ReferenceEquals(a, b);

        public static bool operator !=(Money? a, Money? b) => !(a == b);

        public override bool Equals(object? obj) => obj is Money;

        public override int GetHashCode() => 0;
    }
}
