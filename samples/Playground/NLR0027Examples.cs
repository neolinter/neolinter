// NLR0027: async method without await.
// Methods named Reported* hold the diagnostic (with a code fix for Task and Task<T> bodies that cannot throw); NotReported* must stay silent.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Playground;

public class NLR0027Examples
{
    private readonly List<int> items = new();

    // --- Reported --------------------------------------------------------

    public async Task ReportedTask()
    {
        items.Clear();
    }

    public async Task<int> ReportedGenericTask()
    {
        return items.Count;
    }

    public async Task<int> ReportedThrowWithoutFix(string s)
    {
        if (s is null)
        {
            throw new ArgumentNullException(nameof(s));
        }

        return s.Length;
    }

    public Func<Task<int>> ReportedLambdaWithoutFix()
    {
        return async () => 1;
    }

    // --- Not reported ----------------------------------------------------

    public async Task NotReportedAwait()
    {
        await Task.Delay(1);
    }

    public async Task NotReportedEmptyStub()
    {
    }

    public async Task NotReportedThrowOnly()
    {
        throw new NotSupportedException();
    }

    public async Task NotReportedAwaitForeach(IAsyncEnumerable<int> source)
    {
        await foreach (var item in source)
        {
            items.Add(item);
        }
    }
}
