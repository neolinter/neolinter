// NLR0004: use null-coalescing assignment.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
using System;
using System.Collections.Generic;

namespace Playground;

public class NLR0004Examples
{
    private List<string>? names;
    private int calls;

    public string? Title { get; set; }

    // --- Reported --------------------------------------------------------

    public List<string> ReportedIfNull()
    {
        if (names == null)
        {
            names = new List<string>();
        }

        return names;
    }

    public string ReportedIsNull(string? text)
    {
        if (text is null) text = "none";
        return text;
    }

    public string ReportedCoalesceAssignment(string? text)
    {
        text = text ?? "none";
        return text;
    }

    public void ReportedProperty()
    {
        if (Title == null) Title = "untitled";
    }

    // --- Not reported ----------------------------------------------------

    public void NotReportedElseBranch(string? text)
    {
        if (text == null) text = "none"; else Console.WriteLine(text);
    }

    public void NotReportedOtherTarget(string? first, string? second)
    {
        if (first == null) second = "none";
        Console.WriteLine(second);
    }

    public void NotReportedIndexerWithSideEffect(string?[] values, int index)
    {
        if (values[index++] == null) values[index++] = "none";
    }

    public string NotReportedCall(string? text)
    {
        if (Next() == null) text = "none";
        return text ?? "";
    }

    public void NotReportedComment(string? text)
    {
        if (text == null)
        {
            // the default is deliberate
            text = "none";
        }

        Console.WriteLine(text);
    }

    private string? Next() => calls++ > 0 ? "x" : null;
}
