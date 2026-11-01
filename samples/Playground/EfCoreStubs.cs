// A minimal stand-in for Entity Framework Core, so the playground compiles without the package.
// NLR0006 recognizes Entity Framework Core by type name (Microsoft.EntityFrameworkCore.DbSet<T>), so these stubs
// are enough to see the rule at work. In a real project, reference Microsoft.EntityFrameworkCore instead.
using System.Collections;
using System.Linq.Expressions;

namespace Microsoft.EntityFrameworkCore;

public class DbContext
{
    public DbSet<TEntity> Set<TEntity>() where TEntity : class => new FakeSet<TEntity>();
}

public abstract class DbSet<TEntity> : IQueryable<TEntity> where TEntity : class
{
    public abstract Type ElementType { get; }

    public abstract Expression Expression { get; }

    public abstract IQueryProvider Provider { get; }

    public abstract IEnumerator<TEntity> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class EntityFrameworkQueryableExtensions
{
    public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> source) where T : class => source;

    public static Task<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        Task.FromResult(source.ToList());
}

internal sealed class FakeSet<TEntity> : DbSet<TEntity> where TEntity : class
{
    private readonly IQueryable<TEntity> items = Enumerable.Empty<TEntity>().AsQueryable();

    public override Type ElementType => items.ElementType;

    public override Expression Expression => items.Expression;

    public override IQueryProvider Provider => items.Provider;

    public override IEnumerator<TEntity> GetEnumerator() => items.GetEnumerator();
}
