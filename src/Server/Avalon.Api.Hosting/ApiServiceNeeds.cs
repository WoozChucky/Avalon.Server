using Avalon.Api.Hosting.Worlds;

namespace Avalon.Api.Hosting;

/// <summary>
/// What an API service needs from the shared hosting (#794, design sections 2.2 and 3.1): Redis, the world databases
/// it reads, its part in the auth schema, whether its routes are world routes (<c>/world/{worldId}/...</c>,
/// selected by <see cref="WorldRouteMiddleware"/>), and whether it signs access tokens, the one need that lets a process
/// hold the private signing key (#801). A process provides the union of its services' needs.
/// </summary>
public sealed record ApiServiceNeeds(bool Redis, WorldDatabaseParts WorldDatabases, AuthSchemaRole AuthSchema,
    bool WorldRoutes, bool SignsTokens = false)
{
    /// <summary>
    /// What a process running all of <paramref name="needs"/> provides: Redis, world routes and the signing key when any
    /// needs them, every world database any reads, and the auth schema's owner role when any owns it.
    /// </summary>
    public static ApiServiceNeeds Union(IEnumerable<ApiServiceNeeds> needs)
    {
        var union = new ApiServiceNeeds(false, WorldDatabaseParts.None, AuthSchemaRole.Reader, false);
        foreach (ApiServiceNeeds need in needs)
        {
            union = new ApiServiceNeeds(
                union.Redis || need.Redis,
                union.WorldDatabases | need.WorldDatabases,
                union.AuthSchema == AuthSchemaRole.Owner || need.AuthSchema == AuthSchemaRole.Owner
                    ? AuthSchemaRole.Owner
                    : AuthSchemaRole.Reader,
                union.WorldRoutes || need.WorldRoutes,
                union.SignsTokens || need.SignsTokens);
        }

        return union;
    }
}
