using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Talents;

/// <summary>
/// What the talent window shows (#1181), whole every time: every node the character holds, by ascending id, the points it
/// has earned (one per level, plus the paying quests'), and the points it has spent (one per node held). Sent at select,
/// and after every change of what the window shows. A client replaces what it holds with it; what it may still spend is
/// earned minus spent.
/// </summary>
[ProtoContract]
public sealed class STalentStatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_TALENT_STATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public List<uint> NodeIds { get; set; } = [];
    [ProtoMember(2)] public uint PointsEarned { get; set; }
    [ProtoMember(3)] public uint PointsSpent { get; set; }

    // One id list per thread, refilled for every state: the message is encoded before Create returns, so a send
    // allocates nothing once the list has grown.
#pragma warning disable IDE1006
    [ThreadStatic] private static List<uint>? t_ids;
#pragma warning restore IDE1006

    public static OutboundPacket Create(IReadOnlyList<int> held, uint earned, uint spent, PacketEncoder encoder)
    {
        List<uint> ids = t_ids ??= new List<uint>(held.Count);
        ids.Clear();
        for (int i = 0; i < held.Count; i++)
            ids.Add(checked((uint)held[i]));

        STalentStatePacket message = PacketEncoder.Scratch<STalentStatePacket>();
        message.NodeIds = ids;
        message.PointsEarned = earned;
        message.PointsSpent = spent;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
