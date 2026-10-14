using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auras;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>
/// Each unit's <c>SMSG_AURA_UPDATE</c> for one broadcast pass, encoded once and shared by every client that hears it
/// (#980): its bytes depend only on the unit and its changes, never on who receives it, so the first recipient's send
/// encodes it and every later one takes a reference of its own to the same payload (<see cref="OutboundPacket.Share" />).
/// </summary>
/// <remarks>
/// <para>
/// Only the encoding is kept here. Who hears a unit's update (the unit itself, a client that has it in view and was not
/// listed it this tick) is the broadcast's to decide, so how watchers are found can change without touching this.
/// </para>
/// <para>
/// The pass ends with <see cref="ReleaseAll" />, also when it throws: this holder's references are given up, and each
/// payload returns to its pool once the last recipient's connection has framed it. A unit nobody hears is never encoded.
/// One per instance, tick thread only; it allocates only as its table grows to the most units changed in one pass.
/// </para>
/// </remarks>
/// <param name="wire">The instance's reused aura entries, which each update is written from.</param>
/// <param name="encoder">What each update is encoded with: <see cref="PacketEncoder.Shared" /> in a world.</param>
public sealed class SharedAuraUpdates(AuraWire wire, PacketEncoder encoder)
{
    private readonly Dictionary<ulong, OutboundPacket> _encoded = [];

    /// <summary>
    /// The update of <paramref name="unit" />, whose changes are <paramref name="auras" />, as a reference of its own for
    /// one recipient to send: encoded the first time a recipient asks for it in this pass.
    /// </summary>
    public OutboundPacket For(IUnit unit, UnitAuras auras)
    {
        ulong guid = unit.Guid.RawValue;
        if (!_encoded.TryGetValue(guid, out OutboundPacket packet))
        {
            // Encoded before it is kept, so a throw leaves nothing half made in the table.
            packet = SAuraUpdatePacket.Create(guid, wire.Updates(auras.Changes), encoder);
            _encoded.Add(guid, packet);
        }

        return packet.Share();
    }

    /// <summary>The pass is over: this holder's reference to every update it encoded is given up, and the table emptied.</summary>
    public void ReleaseAll()
    {
        foreach (KeyValuePair<ulong, OutboundPacket> entry in _encoded)
            entry.Value.Release();

        _encoded.Clear();
    }
}
