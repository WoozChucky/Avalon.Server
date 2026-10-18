namespace Avalon.World.Doodads;

/// <summary>
/// Where a placed doodad is in its life, per instance and never persisted. A client is told Ready and Used
/// (<c>Avalon.Network.Packets.State.DoodadState</c>); a Despawned doodad is never described, but removed from every view.
/// </summary>
public enum DoodadState : byte
{
    /// <summary>Placed and usable, by whoever its gates allow.</summary>
    Ready = 1,

    /// <summary>Spent, and showing its used look until it respawns, if it does.</summary>
    Used = 2,

    /// <summary>Spent and gone from every view until it respawns, if it does.</summary>
    Despawned = 3,
}
