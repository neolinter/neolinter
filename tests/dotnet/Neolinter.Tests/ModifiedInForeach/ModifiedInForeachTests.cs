using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.ModifiedInForeach;

namespace Neolinter.Tests.ModifiedInForeach;

/// <summary>NLR0035 tests (report only, no code fix).</summary>
public class ModifiedInForeachTests
{
    private static Task VerifyAsync(string source) => new CSharpAnalyzerTest<ModifiedInForeachAnalyzer, DefaultVerifier>
    {
        TestCode = "using System.Collections.Generic;\nusing System.Collections.Concurrent;\nusing System.Collections.Immutable;\nusing System.Linq;\n\n"
            + "public class C\n{\n    private List<int> field = new();\n\n" + source + "}\n",
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    }.RunAsync();

    // --- Reported -------------------------------------------------------------------

    [Fact]
    public Task ListAdd() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            {|NLR0035:list.Add(x)|};\n        }\n    }\n");

    [Fact]
    public Task ListRemove() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            if (x > 1)\n                {|NLR0035:list.Remove(x)|};\n        }\n    }\n");

    [Theory]
    [InlineData("list.RemoveAt(0)")]
    [InlineData("list.RemoveAll(y => y > 1)")]
    [InlineData("list.Insert(0, x)")]
    [InlineData("list.Clear()")]
    [InlineData("list.Sort()")]
    [InlineData("list.Reverse()")]
    [InlineData("list.AddRange(list)")]
    public Task ListMutators(string call) => VerifyAsync($"    void M(List<int> list)\n    {{\n        foreach (var x in list)\n        {{\n            {{|NLR0035:{call}|}};\n        }}\n    }}\n");

    [Fact]
    public Task LocalVariable() => VerifyAsync("    void M()\n    {\n        var list = new List<int>();\n        foreach (var x in list)\n        {\n            {|NLR0035:list.Add(x)|};\n        }\n    }\n");

    [Fact]
    public Task Field() => VerifyAsync("    void M()\n    {\n        foreach (var x in field)\n        {\n            {|NLR0035:field.Add(x)|};\n        }\n    }\n");

    [Fact]
    public Task FieldThroughThis() => VerifyAsync("    void M()\n    {\n        foreach (var x in this.field)\n        {\n            {|NLR0035:this.field.Add(x)|};\n        }\n    }\n");

    [Fact]
    public Task InterfaceTypedList() => VerifyAsync("    void M(IList<int> list)\n    {\n        foreach (var x in list)\n        {\n            {|NLR0035:list.Add(x)|};\n        }\n    }\n");

    [Fact]
    public Task HashSetAdd() => VerifyAsync("    void M(HashSet<int> set)\n    {\n        foreach (var x in set)\n        {\n            {|NLR0035:set.Add(x + 1)|};\n        }\n    }\n");

    [Fact]
    public Task DictionaryAdd() => VerifyAsync("    void M(Dictionary<int, int> d)\n    {\n        foreach (var x in d)\n        {\n            {|NLR0035:d.Add(x.Key + 1, 0)|};\n        }\n    }\n");

    [Fact]
    public Task SortedSetRemove() => VerifyAsync("    void M(SortedSet<int> set)\n    {\n        foreach (var x in set)\n        {\n            {|NLR0035:set.Remove(x)|};\n        }\n    }\n");

    [Fact]
    public Task QueueDequeue() => VerifyAsync("    void M(Queue<int> q)\n    {\n        foreach (var x in q)\n        {\n            {|NLR0035:q.Dequeue()|};\n        }\n    }\n");

    [Fact]
    public Task StackPush() => VerifyAsync("    void M(Stack<int> s)\n    {\n        foreach (var x in s)\n        {\n            {|NLR0035:s.Push(x)|};\n        }\n    }\n");

    [Fact]
    public Task DerivedFromList() => VerifyAsync("    class MyList : List<int> { }\n\n    void M(MyList list)\n    {\n        foreach (var x in list)\n        {\n            {|NLR0035:list.Add(x)|};\n        }\n    }\n");

    [Fact]
    public Task NestedLoopMutatesOuterCollection() => VerifyAsync(
        "    void M(List<int> outer, List<int> inner)\n    {\n        foreach (var x in outer)\n        {\n            foreach (var y in inner)\n            {\n                {|NLR0035:outer.Add(y)|};\n            }\n        }\n    }\n");

    [Fact]
    public Task NestedLoopOverSameCollectionReportedOnce() => VerifyAsync(
        "    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            foreach (var y in list)\n            {\n                {|NLR0035:list.Add(y)|};\n            }\n        }\n    }\n");

    [Fact]
    public Task BreakOfInnerLoopDoesNotLeaveTheOuterOne() => VerifyAsync(
        "    void M(List<int> list, int[] others)\n    {\n        foreach (var x in list)\n        {\n            foreach (var y in others)\n            {\n                {|NLR0035:list.Add(y)|};\n                break;\n            }\n        }\n    }\n");

    [Fact]
    public Task BreakOfSwitchDoesNotLeaveTheLoop() => VerifyAsync(
        "    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            switch (x)\n            {\n                case 1:\n                    {|NLR0035:list.Add(x)|};\n                    break;\n            }\n        }\n    }\n");

    [Fact]
    public Task ContinueAfterMutation() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            {|NLR0035:list.Add(x)|};\n            continue;\n        }\n    }\n");

    [Fact]
    public Task StatementAfterMutation() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            {|NLR0035:list.Add(x)|};\n            System.Console.WriteLine(x);\n            break;\n        }\n    }\n");

    // --- Not reported ---------------------------------------------------------------

    [Fact]
    public Task NotReportedIterateCopyWithToList() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list.ToList())\n        {\n            list.Add(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedIterateCopyWithToArray() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list.ToArray())\n        {\n            list.Remove(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedIterateCopyVariable() => VerifyAsync("    void M(List<int> list)\n    {\n        var copy = new List<int>(list);\n        foreach (var x in copy)\n        {\n            list.Remove(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedDifferentCollection() => VerifyAsync("    void M(List<int> a, List<int> b)\n    {\n        foreach (var x in a)\n        {\n            b.Add(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedBreakAfterMutation() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            if (x > 1)\n            {\n                list.Remove(x);\n                break;\n            }\n        }\n    }\n");

    [Fact]
    public Task NotReportedReturnAfterMutation() => VerifyAsync("    int M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            list.Add(x);\n            return x;\n        }\n\n        return 0;\n    }\n");

    [Fact]
    public Task NotReportedReturnOfMutation() => VerifyAsync("    bool M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            if (x > 1)\n                return list.Remove(x);\n        }\n\n        return false;\n    }\n");

    [Fact]
    public Task NotReportedThrowAfterMutation() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            list.Add(x);\n            throw new System.Exception();\n        }\n    }\n");

    [Fact]
    public Task NotReportedInLambda() => VerifyAsync("    void M(List<int> list, List<System.Action> actions)\n    {\n        foreach (var x in list)\n        {\n            actions.Add(() => list.Add(x));\n        }\n    }\n");

    [Fact]
    public Task NotReportedInLocalFunction() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            void Later() => list.Add(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedDictionaryRemove() => VerifyAsync("    void M(Dictionary<int, int> d)\n    {\n        foreach (var x in d)\n        {\n            d.Remove(x.Key);\n        }\n    }\n");

    [Fact]
    public Task NotReportedDictionaryIndexerSet() => VerifyAsync("    void M(Dictionary<int, int> d)\n    {\n        foreach (var x in d)\n        {\n            d[x.Key] = 0;\n        }\n    }\n");

    [Fact]
    public Task NotReportedHashSetRemove() => VerifyAsync("    void M(HashSet<int> set)\n    {\n        foreach (var x in set)\n        {\n            set.Remove(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedListIndexerSet() => VerifyAsync("    void M(List<int> list)\n    {\n        foreach (var x in list)\n        {\n            list[0] = x;\n        }\n    }\n");

    [Fact]
    public Task NotReportedForLoop() => VerifyAsync("    void M(List<int> list)\n    {\n        for (var i = 0; i < list.Count; i++)\n        {\n            list.Add(i);\n        }\n    }\n");

    [Fact]
    public Task NotReportedArray() => VerifyAsync("    void M(int[] array, List<int> list)\n    {\n        foreach (var x in array)\n        {\n            list.Add(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedConcurrentCollection() => VerifyAsync("    void M(ConcurrentBag<int> bag)\n    {\n        foreach (var x in bag)\n        {\n            bag.Add(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedConcurrentQueue() => VerifyAsync("    void M(ConcurrentQueue<int> queue)\n    {\n        foreach (var x in queue)\n        {\n            queue.Enqueue(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedImmutableList() => VerifyAsync("    void M(ImmutableList<int> list)\n    {\n        foreach (var x in list)\n        {\n            list = list.Add(x);\n        }\n    }\n");

    [Fact]
    public Task NotReportedLinqReverseExtension() => VerifyAsync("    void M(IEnumerable<int> items, List<int> list)\n    {\n        foreach (var x in list)\n        {\n            var r = list.AsEnumerable().Reverse();\n        }\n    }\n");

    [Fact]
    public Task NotReportedCollectionFromMethodCall() => VerifyAsync("    List<int> Get() => new();\n\n    void M()\n    {\n        foreach (var x in Get())\n        {\n            Get().Add(x);\n        }\n    }\n");
}
