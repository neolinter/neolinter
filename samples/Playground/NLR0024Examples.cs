// NLR0024: avoid async void outside event handlers.
// Methods named Reported* hold the diagnostic (there is no code fix); NotReported* must stay silent.
using System;
using System.Threading.Tasks;

namespace Playground;

public interface INLR0024Handler
{
    void Handle();
}

public class NLR0024Examples : INLR0024Handler
{
    public event EventHandler? Changed;

    public event Action? Ticked;

    // --- Reported --------------------------------------------------------

    public async void ReportedMethod()
    {
        await Task.Delay(1);
    }

    public void ReportedLocalFunction()
    {
        async void Run() => await Task.Delay(1);
        Run();
    }

    public void ReportedLambda()
    {
        Action run = async () => await Task.Delay(1);
        run();
    }

    // --- Not reported ----------------------------------------------------

    public async void NotReportedEventHandler(object? sender, EventArgs e)
    {
        await Task.Delay(1);
    }

    public async void Handle()
    {
        await Task.Delay(1);
    }

    public async void NotReportedSubscribed()
    {
        await Task.Delay(1);
    }

    public void NotReportedSubscribe()
    {
        Ticked += NotReportedSubscribed;
        Ticked += async () => await Task.Delay(1);
        Changed += async (s, e) => await Task.Delay(1);
        Changed?.Invoke(this, EventArgs.Empty);
        Ticked?.Invoke();
    }

    public async Task NotReportedTask()
    {
        await Task.Delay(1);
    }

    public Task NotReportedFuncTask()
    {
        Func<Task> run = async () => await Task.Delay(1);
        return run();
    }
}
