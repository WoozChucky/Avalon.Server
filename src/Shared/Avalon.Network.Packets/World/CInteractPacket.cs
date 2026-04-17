using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.World;

[ProtoContract]
public class CInteractPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_INTERACT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public ulong AccountId { get; set; }
    [ProtoMember(2)] public ulong PlayerId { get; set; }
    [ProtoMember(3)] public int X { get; set; }
    [ProtoMember(4)] public int Y { get; set; }
    [ProtoMember(5)] public int Width { get; set; }
    [ProtoMember(6)] public int Height { get; set; }

    public static NetworkPacket Create(ulong accountId, ulong playerId, int x, int y, int width, int height, EncryptFunc encryptFunc)
    {
        using var memoryStream = new MemoryStream();

        var movementPacket = new CInteractPacket()
        {
            AccountId = accountId,
            PlayerId = playerId,
            X = x,
            Y = y,
            Width = width,
            Height = height
        };

        Serializer.Serialize(memoryStream, movementPacket);

        var buffer = encryptFunc(memoryStream.ToArray());

        return new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = PacketType,
                Flags = Flags,
                Protocol = Protocol,
                Version = 0
            },
            Payload = buffer
        };
    }
}
