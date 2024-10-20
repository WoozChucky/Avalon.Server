// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using Avalon.Network.Packets.Abstractions;

namespace Avalon.World.Public;

public abstract class PacketFilter
{
    public abstract bool Process(NetworkPacket packet);

    public abstract bool CanProcess(NetworkPacketType type);
}
