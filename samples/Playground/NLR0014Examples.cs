// NLR0014: use 'string.IsNullOrEmpty'.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
using System.Linq.Expressions;

namespace Playground;

public class NLR0014Examples
{
    private string? _label;

    // --- Reported --------------------------------------------------------

    public bool ReportedEmpty(string? name) => name == null || name == "";

    public bool ReportedNotEmpty(string? name) => name != null && name != string.Empty;

    public bool ReportedLength(string? name) => name is null || name.Length == 0;

    public bool ReportedField() => _label == null || _label == "";

    public bool ReportedInsideChain(string? name, bool skip) => skip || name == null || name == "";

    // --- Not reported ----------------------------------------------------

    public bool NotReportedWhiteSpace(string? name) => string.IsNullOrWhiteSpace(name);

    public bool NotReportedTrim(string? name) => name == null || name.Trim() == "";

    public bool NotReportedOtherString(string? name, string? other) => name == null || other == "";

    public bool NotReportedExpressionTree(IQueryable<string?> names) => names.Any(n => n == null || n == "");

    public Expression<Func<string?, bool>> NotReportedExpressionLambda() => n => n != null && n != "";

    public bool NotReportedComment(string? name) => name == null || /* deliberate */ name == "";
}
