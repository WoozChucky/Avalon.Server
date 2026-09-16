using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Database;

/// <summary>
/// Runs several writes against one context inside one transaction. Repositories create a context
/// per call, so two repository calls share neither a context nor a transaction; work that must
/// commit together is expressed here instead.
/// </summary>
public interface IDbTransactionRunner<TContext> where TContext : DbContext
{
    Task ExecuteAsync(Func<TContext, CancellationToken, Task> work, CancellationToken cancellationToken = default);

    Task<TResult> ExecuteAsync<TResult>(Func<TContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default);
}

public sealed class DbTransactionRunner<TContext>(IDbContextFactory<TContext> contextFactory)
    : IDbTransactionRunner<TContext>
    where TContext : DbContext
{
    public async Task ExecuteAsync(Func<TContext, CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync<object?>(async (context, token) =>
        {
            await work(context, token);
            return null;
        }, cancellationToken);
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<TContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        // An uncommitted transaction rolls back when it is disposed, so the throw path needs no catch.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        TResult result = await work(context, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
