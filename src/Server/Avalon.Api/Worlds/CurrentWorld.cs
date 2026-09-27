using Avalon.Domain.Auth;

namespace Avalon.Api.Worlds;

/// <summary>
/// The world this request is for (#523): selected on /world/{worldId}/... routes, after the world
/// was found configured, permitted and available; null everywhere else.
/// </summary>
public interface ICurrentWorld
{
    WorldId? Id { get; }

    /// <summary>The auth Worlds row's name; empty when no world is selected.</summary>
    string Name { get; }
}

/// <summary>Scoped. Selected once per request; a second selection is a programming error.</summary>
public sealed class CurrentWorld : ICurrentWorld
{
    public WorldId? Id { get; private set; }
    public string Name { get; private set; } = "";

    public void Select(WorldId id, string name)
    {
        if (Id is not null)
            throw new InvalidOperationException($"This request's world is already selected (world {Id.Value}).");
        Id = id;
        Name = name;
    }
}
