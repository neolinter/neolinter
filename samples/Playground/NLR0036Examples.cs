// NLR0036: new Random() created in a loop.
// Types named Reported* hold the diagnostic (report only, no code fix); NotReported* must stay silent.
namespace Playground;

// --- Reported ------------------------------------------------------------

public class ReportedRandomInForeach
{
    public void Roll(List<int> scores)
    {
        foreach (var score in scores)
        {
            var dice = new Random();
            Console.WriteLine(score + dice.Next(1, 7));
        }
    }
}

public class ReportedRandomInWhile
{
    public List<int> Fill(int count)
    {
        var values = new List<int>();
        while (values.Count < count)
        {
            values.Add(new Random().Next());
        }

        return values;
    }
}

// --- Not reported --------------------------------------------------------

public class NotReportedInstanceOutsideLoop
{
    public void Roll(List<int> scores)
    {
        var dice = new Random();
        foreach (var score in scores)
        {
            Console.WriteLine(score + dice.Next(1, 7));
        }
    }
}

public class NotReportedSeededOrShared
{
    public void Roll(List<int> scores)
    {
        foreach (var score in scores)
        {
            Console.WriteLine(new Random(score).Next());
            Console.WriteLine(Random.Shared.Next());
        }
    }
}

public class NotReportedLambdaInLoop
{
    public List<Func<int>> Build(int count)
    {
        var factories = new List<Func<int>>();
        for (var i = 0; i < count; i++)
        {
            factories.Add(() => new Random().Next());
        }

        return factories;
    }
}
