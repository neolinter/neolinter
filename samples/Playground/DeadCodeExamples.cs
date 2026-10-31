// Targets for "neolinter analyze dead-code" (not implemented yet).
namespace Playground;

public class DeadCodeExamples
{
    // Referenced from Used(): not dead.
    internal static int Helper() => 42;

    public int Used() => Helper();

    // Referenced nowhere in the solution: expected NLD0001 once the analysis exists.
    internal static void NeverCalled()
    {
    }
}
