// NLR0006: keep LINQ operators inside the Entity Framework Core query.
// Methods named Reported* get the diagnostic (no code fix); NotReported* must stay silent.
// The Entity Framework Core types come from EfCoreStubs.cs: only their type names matter to the rule.
using Microsoft.EntityFrameworkCore;

namespace Playground;

public class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public decimal Balance { get; set; }
}

public class ShopContext : DbContext
{
    public DbSet<Customer> Customers { get; set; } = null!;
}

public class NLR0006Examples
{
    private readonly ShopContext db = new();

    // --- Reported --------------------------------------------------------

    public IEnumerable<string> ReportedSelectAfterToList() => db.Customers.ToList().Select(c => c.Name);

    public int ReportedCountAfterToArray() => db.Customers.Where(c => c.Balance > 0).ToArray().Count();

    public async Task<Customer?> ReportedFirstAfterAwaitedToList() =>
        (await db.Customers.AsNoTracking().ToListAsync()).FirstOrDefault(c => c.Id == 1);

    public IEnumerable<Customer> ReportedWhereAfterAsEnumerable() => db.Set<Customer>().AsEnumerable().Where(c => c.Balance > 10);

    // --- Not reported ----------------------------------------------------

    public List<string> NotReportedProjectionInTheQuery() => db.Customers.Select(c => c.Name).ToList();

    public IEnumerable<string> NotReportedPlainList(List<Customer> customers) => customers.Select(c => c.Name);

    // Known limit: a loaded result kept in a variable is not followed.
    public IEnumerable<string> NotReportedLoadedIntoAVariable()
    {
        var loaded = db.Customers.ToList();
        return loaded.Select(c => c.Name);
    }
}
