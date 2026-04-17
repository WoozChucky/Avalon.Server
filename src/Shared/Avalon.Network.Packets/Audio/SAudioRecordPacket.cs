using Avalon.Network.Packets.Abstractions;
using ProtoBuf;
using Avalon.Network.Packets.Serialization;

namespace Avalon.Network.Packets.Audio;

[ProtoContract]
public class SAudioRecordPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_AUDIO_RECORD;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;

    [ProtoMember(1)] public byte[] SoundBuffer { get; set; }

    public static NetworkPacket Create(byte[] soundBuffer)
    {
        var packet = new SAudioRecordPacket
        {
            SoundBuffer = soundBuffer
        };

        return PacketSerializationHelper.SerializeUnencrypted(packet, PacketType, Flags, Protocol);
    }
}
