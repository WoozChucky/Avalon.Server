// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using Avalon.Network.Packets.Abstractions;

namespace Avalon.World.Public;

/// <summary>
///     Represents an abstract base class for packet filters.
/// </summary>
public abstract class PacketFilter
{
    /// <summary>
    ///     Processes the specified network packet.
    /// </summary>
    /// <param name="packet">The network packet to process.</param>
    /// <returns>true if the packet was successfully processed; otherwise, false.</returns>
    public abstract bool Process(NetworkPacket packet);

    /// <summary>
    ///     Determines whether the packet filter can process the specified packet type.
    /// </summary>
    /// <param name="type">The type of the network packet.</param>
    /// <returns>true if the packet filter can process the specified packet type; otherwise, false.</returns>
    public abstract bool CanProcess(NetworkPacketType type);
}
