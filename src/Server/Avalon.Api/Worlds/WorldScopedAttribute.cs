namespace Avalon.Api.Worlds;

/// <summary>
/// Marks a controller whose routes sit under /world/{worldId}/ and read one world's databases (#523).
/// <see cref="WorldRouteMiddleware"/> acts only on endpoints carrying it: GET /world/{id} shares the
/// prefix but is the auth world lookup.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class WorldScopedAttribute : Attribute
{
    /// <summary>The route value naming the world.</summary>
    public const string RouteValue = "worldId";
}
