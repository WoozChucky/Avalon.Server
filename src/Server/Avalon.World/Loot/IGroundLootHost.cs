using Avalon.Common;

namespace Avalon.World.Loot;

/// <summary>
/// An instance that holds drops on the ground. MapInstance is the only one; the interface lets the
/// pickup handler be tested against a substituted instance. Kept out of IMapInstance, which is part
/// of the modding API. Tick thread only.
/// </summary>
public interface IGroundLootHost
{
    /// <summary>The drops on this instance's ground.</summary>
    GroundLootStore Drops { get; }

    /// <summary>Sends one SLootDespawnedPacket with these guids to everyone in the instance.</summary>
    void BroadcastLootDespawned(IReadOnlyCollection<ObjectGuid> lootGuids);
}
