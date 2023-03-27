using ProtoBuf;

namespace Avalon.Network.Packets.Crypto;

[ProtoContract]
public class SCryptoKeyPacket
{
    [ProtoMember(1)] public byte[] Key { get; set; }
}
