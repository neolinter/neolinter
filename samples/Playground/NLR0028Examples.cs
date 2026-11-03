// NLR0028: empty catch block.
// Methods named Reported* hold the diagnostic (report only, no code fix); NotReported* must stay silent.
using System;
using System.IO;
using System.Threading.Tasks;

namespace Playground;

public class NLR0028Examples
{
    // --- Reported --------------------------------------------------------

    public void ReportedEmptyCatch(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    public void ReportedCatchAll(string text)
    {
        try
        {
            Console.WriteLine(int.Parse(text));
        }
        catch
        {
        }
    }

    public void ReportedWithFilter(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException e) when (e.HResult != 0)
        {
        }
    }

    // --- Not reported ----------------------------------------------------

    public void NotReportedCommentOnly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The file may already be gone, nothing to do.
        }
    }

    public void NotReportedCancellation(Task task)
    {
        try
        {
            task.Wait();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void NotReportedHandled(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException e)
        {
            Console.WriteLine(e.Message);
        }
    }
}
