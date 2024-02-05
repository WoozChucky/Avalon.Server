using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Audio;

[ProtoContract]
public class CAudioRecordPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.CMSG_AUDIO_RECORD;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.ClearText;
    
    [ProtoMember(1)] public byte[] SoundBuffer { get; set; }
    
    public static NetworkPacket Create(byte[] soundBuffer, Func<byte[], byte[]> encryptFunc)
    {
        using var memoryStream = new MemoryStream();
        
        var packet = new CAudioRecordPacket()
        {
            SoundBuffer = soundBuffer
        };
        
        Serializer.Serialize(memoryStream, packet);
        
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
