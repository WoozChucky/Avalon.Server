using System;

namespace Avalon.Network.Packets.Abstractions;

[Flags]
public enum NetworkPacketFlags : short
{
    None = 0,
    ClearText = 1,
    Handshake = 2,
    Encrypted = 4
}
