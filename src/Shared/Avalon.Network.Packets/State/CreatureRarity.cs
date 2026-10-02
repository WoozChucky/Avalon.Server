namespace Avalon.Network.Packets.State;

/// <summary>
///     How dangerous a creature is, as a client is told it on <see cref="ObjectState.Rarity" /> (#709),
///     for a nameplate coloured by rarity. Append-only.
/// </summary>
/// <remarks>
///     The wire copy of the server's own <c>Avalon.World.Public.Enums.CreatureRarity</c>, declared here
///     for the reason <see cref="PowerType" /> is: the schema exported for non-.NET clients reaches only
///     the types the packet contracts are built from, and the packets do not reference the world's
///     domain. The server maps one onto the other value by value (<c>ObjectStateWriter</c>), so a
///     renumber on either side cannot leak onto the wire unnoticed; a test pins the two together.
/// </remarks>
public enum CreatureRarity
{
    Normal = 0,
    Elite = 1,
    Rare = 2,
    Boss = 3
}
