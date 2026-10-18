namespace Avalon.Network.Packets.State;

/// <summary>
///     What a client is told a doodad looks like, on <see cref="ObjectState.DoodadState" />: ready to use, or spent and
///     showing its used look. Append-only.
/// </summary>
/// <remarks>
///     The wire copy of the server's own <c>Avalon.World.Doodads.DoodadState</c>, declared here for the reason
///     <see cref="CreatureRarity" /> is: the exported schema reaches only the types the packet contracts are built from.
///     The server's Despawned has no value here, as a despawned doodad is never described: a client is told it is gone
///     by a remove. Unknown is the default a client reads from a value it does not know, and the server never sends it.
/// </remarks>
public enum DoodadState
{
    Unknown = 0,
    Ready = 1,
    Used = 2
}
