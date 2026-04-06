using Avalon.Common.ValueObjects;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Instances;

/// <summary>
/// A live map simulation context — either a shared Town hub or a private Normal map instance.
/// </summary>
public interface IMapInstance : ISimulationContext
{
    Guid InstanceId { get; }
    MapTemplateId TemplateId { get; }
    MapType MapType { get; }

    /// <summary>Account ID of the player who created this instance. Null for Town instances.</summary>
    long? OwnerAccountId { get; }

    /// <summary>Stub for future group support. Contains OwnerAccountId for Normal maps.</summary>
    IReadOnlyList<long> AllowedAccounts { get; }

    int PlayerCount { get; }

    /// <summary>Set when the last player leaves; cleared when a player enters. Null while players are present.</summary>
    DateTime? LastEmptyAt { get; }

    /// <summary>True if this is a Normal map that has been empty longer than <paramref name="expiry"/>.</summary>
    bool IsExpired(TimeSpan expiry);

    /// <summary>True if the instance can still accept a new player without exceeding <paramref name="maxPlayers"/>.</summary>
    bool CanAcceptPlayer(ushort maxPlayers);

    void AddCharacter(IWorldConnection connection);
    void RemoveCharacter(IWorldConnection connection);
    void SpawnStartingEntities();
    void Update(TimeSpan deltaTime);
}
