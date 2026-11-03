// NLR0026: use await Task.Delay instead of Thread.Sleep in async code.
// Methods named Reported* hold the diagnostic (with a code fix when await is possible); NotReported* must stay silent.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playground;

public class NLR0026Examples
{
    private readonly object gate = new();

    // --- Reported --------------------------------------------------------

    public async Task ReportedMilliseconds()
    {
        Thread.Sleep(500);
    }

    public async Task ReportedTimeSpan(TimeSpan wait)
    {
        Thread.Sleep(wait);
    }

    public async Task ReportedInsideLockWithoutFix()
    {
        lock (gate)
        {
            Thread.Sleep(10);
        }

        await Task.Yield();
    }

    // --- Not reported ----------------------------------------------------

    public void NotReportedSyncMethod()
    {
        Thread.Sleep(500);
    }

    public async Task NotReportedSyncLambda()
    {
        Action pause = () => Thread.Sleep(500);
        await Task.Run(pause);
    }

    public async Task NotReportedZeroDelay()
    {
        Thread.Sleep(0);
        await Task.Yield();
    }
}
