// NLR0033: Equals without GetHashCode.
// Types named Reported* hold the diagnostic (report only, no code fix); NotReported* must stay silent.
namespace Playground;

// --- Reported ------------------------------------------------------------

public class ReportedEqualsOnly
{
    public int Id;

    public override bool Equals(object? obj) => obj is ReportedEqualsOnly o && o.Id == Id;
}

public class ReportedOperatorsOnly
{
    public int Id;

    public static bool operator ==(ReportedOperatorsOnly? a, ReportedOperatorsOnly? b) => a?.Id == b?.Id;

    public static bool operator !=(ReportedOperatorsOnly? a, ReportedOperatorsOnly? b) => !(a == b);
}

public class ReportedEquatableOnly : IEquatable<ReportedEquatableOnly>
{
    public int Id;

    public bool Equals(ReportedEquatableOnly? other) => other?.Id == Id;
}

public class ReportedHashCodeOnly
{
    public int Id;

    public override int GetHashCode() => Id;
}

// --- Not reported --------------------------------------------------------

public class NotReportedBoth
{
    public int Id;

    public override bool Equals(object? obj) => obj is NotReportedBoth o && o.Id == Id;

    public override int GetHashCode() => Id;
}

public record NotReportedRecord(int Id);

public abstract class NotReportedAbstract
{
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);
}

public class NotReportedOverload
{
    public int Id;

    public bool Equals(NotReportedOverload? other) => other?.Id == Id;
}
