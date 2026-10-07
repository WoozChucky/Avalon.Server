namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// Marks a controller whose routes sit under /public/world/{worldId}/ and read one world's databases for
/// anyone (tooltips). <see cref="WorldRouteMiddleware"/> selects the world as for
/// <see cref="WorldScopedAttribute"/>, except that a caller who is not signed in, or whose token is not
/// valid, counts as a Player: anyone reads a world every player may enter, and only a caller who may
/// enter a restricted world reads it.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PublicWorldScopedAttribute : Attribute
{
    /// <summary>
    /// <c>HttpContext.Items</c> key: true when the selected world is one every player may enter, so its
    /// responses may be cached publicly.
    /// </summary>
    public const string OpenToEveryoneItem = "Avalon.PublicWorld.OpenToEveryone";
}
