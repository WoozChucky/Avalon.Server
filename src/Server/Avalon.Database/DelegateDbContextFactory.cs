using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> over a construction delegate, for contexts that
/// configure themselves in <c>OnConfiguring</c> rather than from <c>DbContextOptions</c>.
/// Disposing what it returns is the caller's job.
/// </summary>
public sealed class DelegateDbContextFactory<TContext>(Func<TContext> create) : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(create());
}
