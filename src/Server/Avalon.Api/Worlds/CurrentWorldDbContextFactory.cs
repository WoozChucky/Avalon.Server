using Avalon.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Api.Worlds;

/// <summary>
/// The api's <see cref="IDbContextFactory{TContext}"/> for the World and Characters databases (#523).
/// A singleton, like the repositories built on it, that opens the current request's world, read
/// from the request's scoped <see cref="ICurrentWorld"/>. With no world selected (a route outside
/// /world/{worldId}/, or no request at all) it throws rather than fall back to some world. Code that
/// needs a named world uses <see cref="IWorldDbContextFactory"/> or <see cref="IWorldRepositories"/>.
/// </summary>
public sealed class CurrentWorldDbContextFactory<TContext>(
    IHttpContextAccessor http,
    IWorldDbContextFactory worlds,
    Func<IWorldDbContextFactory, WorldId, TContext> create) : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public const string NoWorldSelected =
        "No world selected for this request: the world and characters databases are reached only under " +
        "/world/{worldId}/, or through IWorldDbContextFactory for a named world.";

    public TContext CreateDbContext()
    {
        WorldId world = http.HttpContext?.RequestServices.GetService<ICurrentWorld>()?.Id
                        ?? throw new InvalidOperationException(NoWorldSelected);
        return create(worlds, world);
    }

    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}
