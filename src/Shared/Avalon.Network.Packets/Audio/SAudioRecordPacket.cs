using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Audio;

[ProtoContract]
public class SAudioRecordPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AUDIO_RECORD;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;

    [ProtoMember(1)] public byte[] SoundBuffer { get; set; }

    public static OutboundPacket Create(byte[] soundBuffer, PacketEncoder encoder)
    {
        var packet = new SAudioRecordPacket
        {
            SoundBuffer = soundBuffer
        };

        return encoder.Encode(packet, PacketType, Flags, Protocol);
    }
}
