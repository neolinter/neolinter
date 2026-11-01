using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Neolinter.Analyzers.EfCoreClientLinq;

namespace Neolinter.Tests.EfCoreClientLinq;

/// <summary>
/// NLR0006 tests. Entity Framework Core is not referenced: minimal stand-ins live in the
/// <c>Microsoft.EntityFrameworkCore</c> namespace, which is all the rule looks at.
/// </summary>
public class EfCoreClientLinqTests
{
    private const string Stubs = """
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Linq;
        using System.Linq.Expressions;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext
            {
                public DbSet<TEntity> Set<TEntity>() where TEntity : class => throw null;
            }

            public abstract class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
            {
                public abstract Type ElementType { get; }
                public abstract Expression Expression { get; }
                public abstract IQueryProvider Provider { get; }
                public abstract IEnumerator<TEntity> GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => throw null;
            }

            public static class EntityFrameworkQueryableExtensions
            {
                public static IQueryable<T> Include<T, TProperty>(this IQueryable<T> source, Expression<Func<T, TProperty>> path) where T : class => throw null;
                public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) where T : class => throw null;
                public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => throw null;
                public static Task<T[]> ToArrayAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) => throw null;
                public static Task<Dictionary<TKey, T>> ToDictionaryAsync<T, TKey>(this IQueryable<T> source, Func<T, TKey> keySelector, CancellationToken cancellationToken = default) => throw null;
            }
        }
        """;

    private const string Header = """
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        public class Order
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public decimal Total { get; set; }
            public List<string> Tags { get; set; }
        }

        public class Db : DbContext
        {
            public DbSet<Order> Orders { get; set; }
        }

        """;

    private static Task Run(string members, string extra = "")
    {
        var test = new CSharpCodeFixTest<EfCoreClientLinqAnalyzer, EmptyCodeFixProvider, DefaultVerifier>
        {
            TestCode = Header + "class C\n{\n    Db Db { get; set; }\n" + members + "\n}\n" + extra,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(Stubs);
        return test.RunAsync();
    }

    private static Task Reported(string expression) => Run($"    object M() => {expression};");

    private static Task NotReported(string members, string extra = "") => Run(members, extra);

    // --- Reported, one case per operator group -------------------------------

    [Theory]
    [InlineData("Select", "o => o.Id")]
    [InlineData("SelectMany", "o => o.Tags")]
    public Task Projection(string op, string args) => Reported($"Db.Orders.ToList().{{|NLR0006:{op}|}}({args})");

    [Fact]
    public Task Filter() => Reported("Db.Orders.ToList().{|NLR0006:Where|}(o => o.Total > 10)");

    [Theory]
    [InlineData("OrderBy")]
    [InlineData("OrderByDescending")]
    public Task Ordering(string op) => Reported($"Db.Orders.ToList().{{|NLR0006:{op}|}}(o => o.Id)");

    [Fact]
    public Task Grouping() => Reported("Db.Orders.ToList().{|NLR0006:GroupBy|}(o => o.Name)");

    [Theory]
    [InlineData("Count", "")]
    [InlineData("LongCount", "")]
    [InlineData("Any", "")]
    [InlineData("Any", "o => o.Id > 1")]
    [InlineData("All", "o => o.Id > 1")]
    public Task CountingAndQuantifiers(string op, string args) => Reported($"Db.Orders.ToList().{{|NLR0006:{op}|}}({args})");

    [Theory]
    [InlineData("First")]
    [InlineData("FirstOrDefault")]
    [InlineData("Single")]
    [InlineData("SingleOrDefault")]
    [InlineData("Last")]
    [InlineData("LastOrDefault")]
    public Task SingleElement(string op) => Reported($"Db.Orders.ToList().{{|NLR0006:{op}|}}()");

    [Theory]
    [InlineData("Skip", "1")]
    [InlineData("Take", "5")]
    [InlineData("Distinct", "")]
    public Task PagingAndDistinct(string op, string args) => Reported($"Db.Orders.ToList().{{|NLR0006:{op}|}}({args})");

    [Theory]
    [InlineData("Sum")]
    [InlineData("Min")]
    [InlineData("Max")]
    [InlineData("Average")]
    public Task Aggregates(string op) => Reported($"Db.Orders.ToList().{{|NLR0006:{op}|}}(o => o.Total)");

    [Fact]
    public Task ThenByAfterInMemoryOrderIsNotReportedTwice() =>
        Reported("Db.Orders.ToList().{|NLR0006:OrderBy|}(o => o.Id).ThenBy(o => o.Name)");

    [Fact]
    public Task OnlyTheFirstOperatorAfterLoadingIsReported() =>
        Reported("Db.Orders.ToList().{|NLR0006:Select|}(o => o.Id).Where(i => i > 1).Count()");

    [Fact]
    public Task MessageNamesOperatorAndMaterializer()
    {
        var test = new CSharpCodeFixTest<EfCoreClientLinqAnalyzer, EmptyCodeFixProvider, DefaultVerifier>
        {
            TestCode = Header + "class C\n{\n    Db Db { get; set; }\n    object M() => Db.Orders.ToArray().{|#0:Any|}();\n}\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(Stubs);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(EfCoreClientLinqAnalyzer.Rule).WithLocation(0).WithArguments("Any", "ToArray"));
        return test.RunAsync();
    }

    // --- Materialization forms ---------------------------------------------------

    [Theory]
    [InlineData("ToList()")]
    [InlineData("ToArray()")]
    [InlineData("ToDictionary(o => o.Id)")]
    [InlineData("AsEnumerable()")]
    public Task MaterializingMethods(string materializer) => Reported($"Db.Orders.{materializer}.{{|NLR0006:Select|}}(x => x)");

    [Theory]
    [InlineData("Db.Orders.Where(o => o.Id > 1)")]
    [InlineData("Db.Orders.Include(o => o.Tags).Where(o => o.Id > 1)")]
    [InlineData("Db.Orders.AsNoTracking()")]
    [InlineData("Db.Set<Order>()")]
    [InlineData("Db.Orders.AsNoTracking().Include(o => o.Tags).OrderBy(o => o.Id).Skip(1).Take(2)")]
    [InlineData("Db.Orders.AsQueryable()")]
    public Task QuerySources(string query) => Reported($"{query}.ToList().{{|NLR0006:Select|}}(o => o.Id)");

    [Fact]
    public Task DbSetDeclaredAsParameter() => Run(
        "    object M(DbSet<Order> orders) => orders.ToList().{|NLR0006:Select|}(o => o.Id);");

    [Fact]
    public Task DbSetSubclass() => Run(
        "    object M(OrderSet orders) => orders.ToList().{|NLR0006:Select|}(o => o.Id);",
        "public abstract class OrderSet : DbSet<Order> { }");

    // --- Asynchronous and parenthesized forms ----------------------------------------

    [Theory]
    [InlineData("ToListAsync()")]
    [InlineData("ToListAsync(ct)")]
    [InlineData("ToArrayAsync()")]
    [InlineData("ToDictionaryAsync(o => o.Id)")]
    [InlineData("ToListAsync().ConfigureAwait(false)")]
    public Task AsyncMaterializers(string materializer) => Run(
        $"    async Task<object> M(CancellationToken ct) => (await Db.Orders.{materializer}).{{|NLR0006:Select|}}(x => x);");

    [Fact]
    public Task AsyncAfterQueryOperators() => Run(
        "    async Task<object> M() => (await Db.Orders.Include(o => o.Tags).Where(o => o.Id > 1).ToListAsync()).{|NLR0006:Count|}();");

    [Fact]
    public Task ParenthesizedSyncForms() => Run(
        "    object M() => (Db.Orders.ToList()).{|NLR0006:Select|}(o => o.Id);");

    [Fact]
    public Task NullForgivingOperator() => Run(
        "    object M() => Db.Orders.ToList()!.{|NLR0006:Select|}(o => o.Id);");

    [Fact]
    public Task ReportedInsideLambdaAndLocalFunction() => Run(
        """
            object M()
            {
                int Local() => Db.Orders.ToList().{|NLR0006:Count|}();
                System.Func<object> f = () => Db.Orders.ToList().{|NLR0006:First|}();
                return Local() + f().ToString();
            }
        """);

    // --- Not reported -------------------------------------------------------------

    [Fact]
    public Task OperatorBeforeLoading() => NotReported(
        "    object M() => Db.Orders.Where(o => o.Id > 1).Select(o => o.Id).ToList();");

    [Fact]
    public Task AsyncOperatorBeforeLoading() => NotReported(
        "    async Task<object> M() => await Db.Orders.Where(o => o.Id > 1).ToListAsync();");

    [Fact]
    public Task PlainListParameter() => NotReported(
        "    object M(List<Order> orders) => orders.Select(o => o.Id).Where(i => i > 1);");

    [Fact]
    public Task PlainEnumerableMaterializedAgain() => NotReported(
        "    object M(IEnumerable<Order> orders) => orders.ToList().Select(o => o.Id);");

    [Fact]
    public Task ArrayToListInMemory() => NotReported(
        "    object M() => new[] { new Order() }.ToList().Where(o => o.Id > 1);");

    [Fact]
    public Task QueryableParameterIsNotTracedToItsSource() => NotReported(
        "    object M(IQueryable<Order> query) => query.ToList().Select(o => o.Id);");

    [Fact]
    public Task AsQueryableOverAListIsInMemory() => NotReported(
        "    object M(List<Order> orders) => orders.AsQueryable().Where(o => o.Id > 1).ToList().Select(o => o.Id);");

    [Fact]
    public Task CollectionNavigationInsideTheQuery() => NotReported(
        "    object M() => Db.Orders.Where(o => o.Tags.ToList().Any()).ToList();");

    [Fact]
    public Task ElementOfAQueryThatIsAlreadyInMemory() => NotReported(
        "    object M() => Db.Orders.Select(o => o.Tags).First().ToList().Where(t => t.Length > 1);");

    [Fact]
    public Task LoadedResultOfAnotherSource() => NotReported(
        "    object M(Dictionary<int, Order> map) => map.ToList().Select(p => p.Key);");

    [Fact]
    public Task MembersThatAreNotLinqOperators() => NotReported(
        "    object M() => Db.Orders.ToList().Count + Db.Orders.ToArray().Length;");

    [Fact]
    public Task ListOwnMethodsAreNotLinq() => NotReported(
        "    void M() => Db.Orders.ToList().ForEach(o => { });");

    [Fact]
    public Task OperatorNotInTheListIsIgnored() => NotReported(
        "    object M() => Db.Orders.ToList().Concat(Db.Orders.ToList()).ToList();");

    [Fact]
    public Task SameNamesInAnotherNamespace() => Run(
        "    object M(Other.DbSet<Order> orders) => orders.ToList().Select(o => o.Id);",
        "namespace Other { public abstract class DbSet<T> : System.Collections.Generic.IEnumerable<T> { public abstract System.Collections.Generic.IEnumerator<T> GetEnumerator(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw null; } }");

    // --- Known limits (documented in rules/NLR0006.yaml) -----------------------------

    [Fact]
    public Task KnownLimit_LoadedResultKeptInAVariable() => NotReported(
        """
            object M()
            {
                var loaded = Db.Orders.ToList();
                return loaded.Select(o => o.Id);
            }
        """);

    [Fact]
    public Task KnownLimit_StaticCallSyntax() => NotReported(
        "    object M() => Enumerable.Select(Db.Orders.ToList(), o => o.Id);");

    [Fact]
    public Task KnownLimit_NullConditionalAccess() => NotReported(
        "    object M() => Db.Orders.ToList()?.Select(o => o.Id);");
}
