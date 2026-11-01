// NLR0005: simplify comparison with a boolean literal.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
namespace Playground;

public class NLR0005Examples
{
    public bool Enabled { get; set; }

    // --- Reported --------------------------------------------------------

    public bool ReportedEqualsTrue(bool flag) => flag == true;

    public bool ReportedFalseOnTheLeft(bool flag) => false == flag;

    public bool ReportedNotEqualsTrue(bool flag) => flag != true;

    public bool ReportedPrecedence(bool a, bool b) => a && b == false;

    public bool ReportedProperty() => Enabled == false;

    // --- Not reported ----------------------------------------------------

    public bool NotReportedNullableBool(bool? flag) => flag == true;

    public bool NotReportedNoLiteral(bool a, bool b) => a == b;

    public bool NotReportedConstant() => true == false;

    public bool NotReportedComment(bool flag) => flag /* deliberate */ == true;
}
