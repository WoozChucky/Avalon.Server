using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;

namespace Avalon.World.Serialization;

/// <summary>
/// Puts each recipient's <c>SMSG_WORLD_STATE_UPDATE</c> of one broadcast together from entries encoded once per
/// broadcast (#980), rather than encoding every entity it sees again for every recipient.
/// </summary>
/// <remarks>
/// <para>
/// An update's payload is its entries one after another (<see cref="SInstanceStateUpdatePacket.WriteEntry" />), so a
/// packet copied together from entries is byte for byte the one <see cref="SInstanceStateUpdatePacket.Create" /> writes
/// for the same list. An entity's entry depends on the entity and the fields marked changed, which are the same for
/// every recipient; the one exception is a character's update to its own client, whose motion is left out
/// (<c>MapInstance.MaskSelfSuppression</c>), and which the caller describes and appends for that recipient alone
/// (<see cref="AppendOwn" />). An entry is keyed by the entity and kept with its fields: a recipient that asks for the
/// entity with other fields gets an entry of its own, so a shared entry is never one written for different fields.
/// </para>
/// <para>
/// Only the encoding is kept here. Which entities a recipient is sent, and with which fields, is the caller's to decide
/// (its tracking systems today), so how watchers are found can change without touching this.
/// </para>
/// <para>
/// Every broadcast starts with <see cref="BeginBroadcast" /> and every packet with <see cref="BeginPacket" />, which
/// forget what came before, so a broadcast that threw leaves nothing to the next. One per instance, tick thread only;
/// its buffers and its table grow to the largest broadcast and are kept, so a broadcast allocates nothing once they
/// have.
/// </para>
/// </remarks>
public sealed class StateUpdateAssembler
{
    // This broadcast's shared entries, back to back, and where each entity's is.
    private readonly MemoryStream _entries = new(1024);
    private readonly Dictionary<ulong, Entry> _index = [];

    // The packet being put together.
    private readonly MemoryStream _packet = new(1024);

    /// <summary>Whether the packet begun has no entry: nothing to send.</summary>
    public bool IsEmpty => _packet.Length == 0;

    /// <summary>A new broadcast: the entries of the last one are forgotten.</summary>
    public void BeginBroadcast()
    {
        _entries.SetLength(0);
        _index.Clear();
    }

    /// <summary>A new recipient's packet, empty.</summary>
    public void BeginPacket() => _packet.SetLength(0);

    /// <summary>
    /// Appends the shared entry of <paramref name="guid" />, if this broadcast has encoded it with these
    /// <paramref name="fields" />; false, appending nothing, when it has not (<see cref="AppendShared" />).
    /// </summary>
    public bool TryAppendShared(ObjectGuid guid, GameEntityFields fields)
    {
        if (!_index.TryGetValue(guid.RawValue, out Entry entry) || entry.Fields != fields)
            return false;

        _packet.Write(_entries.GetBuffer(), entry.Offset, entry.Length);
        return true;
    }

    /// <summary>
    /// Encodes <paramref name="update" />, the entity's entry with these <paramref name="fields" /> as every recipient
    /// but the entity itself is sent it, once for the broadcast, and appends it; null for an entity that describes to
    /// nothing (it has left), which is remembered too. An entity already encoded with other fields is encoded for this
    /// packet alone.
    /// </summary>
    public void AppendShared(ObjectGuid guid, GameEntityFields fields, ObjectState? update)
    {
        if (_index.ContainsKey(guid.RawValue))
        {
            if (update is not null)
                AppendOwn(update);
            return;
        }

        int offset = (int)_entries.Length;
        if (update is not null)
            SInstanceStateUpdatePacket.WriteEntry(update, _entries);

        var entry = new Entry(offset, (int)_entries.Length - offset, fields);
        _index.Add(guid.RawValue, entry);
        _packet.Write(_entries.GetBuffer(), entry.Offset, entry.Length);
    }

    /// <summary>Encodes <paramref name="update" /> into this packet alone: the recipient's own character.</summary>
    public void AppendOwn(ObjectState update) => SInstanceStateUpdatePacket.WriteEntry(update, _packet);

    /// <summary>The packet put together since <see cref="BeginPacket" />, in a segment of <paramref name="encoder" />'s pool.</summary>
    public OutboundPacket Finish(PacketEncoder encoder) => encoder.FromPayload(
        new ReadOnlySpan<byte>(_packet.GetBuffer(), 0, (int)_packet.Length),
        SInstanceStateUpdatePacket.PacketType, SInstanceStateUpdatePacket.Flags, SInstanceStateUpdatePacket.Protocol);

    private readonly record struct Entry(int Offset, int Length, GameEntityFields Fields);
}
