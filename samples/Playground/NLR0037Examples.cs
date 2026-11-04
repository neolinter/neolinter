// NLR0037: useless catch that only rethrows.
// Methods named Reported* hold the diagnostic (code fix removes the clause); NotReported* must stay silent.
namespace Playground;

public class NLR0037Examples
{
    private void Save() { }

    private void Log(string message) => Console.WriteLine(message);

    // --- Reported --------------------------------------------------------

    public void ReportedOnlyCatch()
    {
        try
        {
            Save();
        }
        catch (IOException)
        {
            throw;
        }
    }

    public void ReportedWithFinally()
    {
        try
        {
            Save();
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            Log("done");
        }
    }

    public void ReportedLastClause()
    {
        try
        {
            Save();
        }
        catch (IOException)
        {
            Log("disk");
        }
        catch (ArgumentException)
        {
            throw;
        }
    }

    // --- Not reported ----------------------------------------------------

    public void NotReportedShieldsBroaderClause()
    {
        try
        {
            Save();
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception)
        {
            Log("other");
        }
    }

    public void NotReportedFilter(bool flag)
    {
        try
        {
            Save();
        }
        catch (IOException) when (flag)
        {
            throw;
        }
    }

    public void NotReportedDoesMoreThanRethrow()
    {
        try
        {
            Save();
        }
        catch (IOException)
        {
            Log("disk");
            throw;
        }
    }
}
