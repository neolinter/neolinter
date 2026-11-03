// NLR0025: do not block on a task inside an async method.
// Methods named Reported* hold the diagnostic (with a code fix when await is possible); NotReported* must stay silent.
using System.Threading.Tasks;

namespace Playground;

public class NLR0025Examples
{
    // --- Reported --------------------------------------------------------

    public async Task<int> ReportedResult(Task<int> task)
    {
        return task.Result;
    }

    public async Task ReportedWait(Task task)
    {
        task.Wait();
    }

    public async Task<int> ReportedGetResult(Task<int> task)
    {
        return task.ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public async Task ReportedWaitAll(Task first, Task second)
    {
        Task.WaitAll(first, second);
    }

    // --- Not reported ----------------------------------------------------

    public int NotReportedSyncMethod(Task<int> task)
    {
        return task.Result;
    }

    public async Task<int> NotReportedAfterAwait(Task<int> first, Task<int> second)
    {
        await Task.WhenAll(first, second);
        return first.Result + second.Result;
    }

    public async Task<int> NotReportedFromResult()
    {
        await Task.Delay(1);
        return Task.FromResult(1).Result;
    }

    public async Task<int> NotReportedSyncLambda(Task<int> task)
    {
        await Task.Delay(1);
        System.Func<int> read = () => task.Result;
        return read();
    }
}
