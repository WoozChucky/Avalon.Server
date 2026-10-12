using Microsoft.EntityFrameworkCore;

namespace Avalon.Database;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> over a delegate that creates a fresh context per call.
/// Disposing what it returns is the caller's job.
/// </summary>
public sealed class DelegateDbContextFactory<TContext>(Func<TContext> create) : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(create());
}
