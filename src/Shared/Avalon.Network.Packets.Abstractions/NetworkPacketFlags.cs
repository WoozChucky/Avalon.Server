namespace Avalon.Network.Packets.Abstractions;

public enum NetworkPacketFlags
{
    None = 0,
    ClearText = 1,
    Handshake = 2,
    Encrypted = 3
}
