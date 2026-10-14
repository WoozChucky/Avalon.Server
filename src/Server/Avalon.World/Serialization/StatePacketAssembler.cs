using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;

namespace Avalon.World.Serialization;

/// <summary>
/// Puts each recipient's <c>SMSG_WORLD_STATE_ADD</c> or <c>SMSG_WORLD_STATE_UPDATE</c> of one broadcast together from
/// entries encoded once per broadcast (#980), rather than encoding every entity it is sent again for every recipient.
/// One assembler puts one kind of packet together (<see cref="ForAdds" />, <see cref="ForUpdates" />).
/// </summary>
/// <remarks>
/// <para>
/// Both packets have one member, a repeated message, and their payload is its entries one after another
/// (<see cref="SInstanceStateAddPacket.WriteEntry" />, <see cref="SInstanceStateUpdatePacket.WriteEntry" />), so a
/// packet copied together from entries is byte for byte the one the packet's <c>Create</c> writes for the same list.
/// An entity's entry depends on the entity and the fields it is described with, which are the same for every
/// recipient; the one exception is a character's add or update to its own client, whose motion is left out
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
/// forget what came before, so a broadcast that threw leaves nothing to the next. One of each kind per instance, tick
/// thread only; its buffers and its table grow to the largest broadcast and are kept, so a broadcast allocates nothing
/// once they have.
/// </para>
/// </remarks>
public sealed class StatePacketAssembler
{
    private readonly bool _adds;

    /// <summary>
    /// The fewest recipients a broadcast must have for its packets to be put together from shared entries; with fewer,
    /// each recipient's list is encoded whole by the packet's <c>Create</c>, as before #980 (the same bytes).
    /// </summary>
    /// <remarks>
    /// An entry encoded on its own costs about 1.6 times its share of a whole encode (each entry is a serializer call of
    /// its own, then copied twice), so it pays off only once another recipient copies it rather than encoding it again.
    /// Measured (docs/benchmarks.md, shared state and aura encoding): for one recipient the shared entries were 50% to
    /// 65% slower than a whole encode (fifteen creatures; 20% to 71% for one to forty), for two recipients seeing the
    /// same entities 9% to 14% faster (15% to 39% for one to forty creatures), and for three 37% to 38% faster. A party
    /// of two in a forest mostly sees the same creatures, so two it is.
    /// </remarks>
    public const int MinRecipientsToShare = 2;

    // This broadcast's shared entries, back to back, and where each entity's is.
    private readonly MemoryStream _entries = new(1024);
    private readonly Dictionary<ulong, Entry> _index = [];

    // The packet being put together.
    private readonly MemoryStream _packet = new(1024);

    private StatePacketAssembler(bool adds) => _adds = adds;

    /// <summary>Whether the packet begun has no entry: nothing to send.</summary>
    public bool IsEmpty => _packet.Length == 0;

    /// <summary>An assembler of <c>SMSG_WORLD_STATE_ADD</c> packets.</summary>
    public static StatePacketAssembler ForAdds() => new(adds: true);

    /// <summary>An assembler of <c>SMSG_WORLD_STATE_UPDATE</c> packets.</summary>
    public static StatePacketAssembler ForUpdates() => new(adds: false);

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
    /// Encodes <paramref name="state" />, the entity's entry with these <paramref name="fields" /> as every recipient
    /// but the entity itself is sent it, once for the broadcast, and appends it; null for an entity that describes to
    /// nothing (it has left), which is remembered too. An entity already encoded with other fields is encoded for this
    /// packet alone.
    /// </summary>
    public void AppendShared(ObjectGuid guid, GameEntityFields fields, ObjectState? state)
    {
        if (_index.ContainsKey(guid.RawValue))
        {
            if (state is not null)
                AppendOwn(state);
            return;
        }

        int offset = (int)_entries.Length;
        if (state is not null)
            Write(state, _entries);

        var entry = new Entry(offset, (int)_entries.Length - offset, fields);
        _index.Add(guid.RawValue, entry);
        _packet.Write(_entries.GetBuffer(), entry.Offset, entry.Length);
    }

    /// <summary>Encodes <paramref name="state" /> into this packet alone: the recipient's own character.</summary>
    public void AppendOwn(ObjectState state) => Write(state, _packet);

    /// <summary>The packet put together since <see cref="BeginPacket" />, in a segment of <paramref name="encoder" />'s pool.</summary>
    public OutboundPacket Finish(PacketEncoder encoder)
    {
        var payload = new ReadOnlySpan<byte>(_packet.GetBuffer(), 0, (int)_packet.Length);
        return _adds
            ? encoder.FromPayload(payload, SInstanceStateAddPacket.PacketType, SInstanceStateAddPacket.Flags,
                SInstanceStateAddPacket.Protocol)
            : encoder.FromPayload(payload, SInstanceStateUpdatePacket.PacketType, SInstanceStateUpdatePacket.Flags,
                SInstanceStateUpdatePacket.Protocol);
    }

    private void Write(ObjectState state, MemoryStream destination)
    {
        if (_adds)
            SInstanceStateAddPacket.WriteEntry(state, destination);
        else
            SInstanceStateUpdatePacket.WriteEntry(state, destination);
    }

    private readonly record struct Entry(int Offset, int Length, GameEntityFields Fields);
}
