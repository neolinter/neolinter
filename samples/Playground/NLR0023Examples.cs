// NLR0023: rethrow with throw; instead of throw ex;.
// Methods named Reported* get the diagnostic and a code fix; NotReported* must stay silent.
using System;
using System.IO;

namespace Playground;

public class NLR0023Examples
{
    // --- Reported --------------------------------------------------------

    public void ReportedLogAndRethrow(string path)
    {
        try
        {
            File.ReadAllText(path);
        }
        catch (IOException e)
        {
            Console.Error.WriteLine(e.Message);
            throw e;
        }
    }

    public void ReportedWithFilter(string path)
    {
        try
        {
            File.ReadAllText(path);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw e;
        }
    }

    // --- Not reported ----------------------------------------------------

    public void NotReportedBareRethrow(string path)
    {
        try
        {
            File.ReadAllText(path);
        }
        catch (IOException)
        {
            throw;
        }
    }

    public void NotReportedWrapped(string path)
    {
        try
        {
            File.ReadAllText(path);
        }
        catch (IOException e)
        {
            throw new InvalidOperationException("Cannot read the file.", e);
        }
    }

    public void NotReportedInLambda(string path)
    {
        try
        {
            File.ReadAllText(path);
        }
        catch (IOException e)
        {
            Action fail = () => { throw e; };
            fail();
        }
    }
}
