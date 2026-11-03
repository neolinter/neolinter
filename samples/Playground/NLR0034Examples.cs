// NLR0034: Mutable struct.
// Types named Reported* hold the diagnostic (report only, no code fix); NotReported* must stay silent.
namespace Playground;

// --- Reported ------------------------------------------------------------

public struct ReportedMutableField
{
    public int X;
    public int Y;
}

public struct ReportedPropertySetter
{
    public int Count { get; set; }
}

// --- Not reported --------------------------------------------------------

public readonly struct NotReportedReadonlyStruct
{
    public int X { get; }

    public NotReportedReadonlyStruct(int x) => X = x;
}

public record struct NotReportedRecordStruct(int X, int Y);

public struct NotReportedInitOnly
{
    public int X { get; init; }
}
