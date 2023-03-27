using ProtoBuf;

namespace Avalon.Network.Packets.Auth;

[ProtoContract]
public class CAuthPacket
{
    [ProtoMember(1)] public string Username { get; set; }
    [ProtoMember(2)] public string Password { get; set; }
}
