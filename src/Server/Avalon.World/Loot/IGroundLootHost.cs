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

    /// <summary>
    /// Tells the clients that hold the drops (#1269) that they were picked up: one SLootDespawnedPacket each, naming those
    /// of these guids it holds, and the drops leave each such view.
    /// </summary>
    void BroadcastLootDespawned(IReadOnlyList<ObjectGuid> lootGuids);
}
