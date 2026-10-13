using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Networking;

/// <summary>Why <see cref="IPacketReader.Read" /> refused a frame's payload: each is something the sender did.</summary>
public enum PacketReadFailure
{
    /// <summary>The frame is flagged Encrypted and arrived before the connection's session had its keys.</summary>
    SealedBeforeKey,

    /// <summary>
    /// The frame is flagged Encrypted and does not open under the session's receiving key: it does not authenticate,
    /// or it is shorter than a nonce and a tag.
    /// </summary>
    SealNotAuthentic,

    /// <summary>The payload is not an encoding of the packet type its opcode names.</summary>
    Undecodable,
}

/// <summary>
/// A frame's payload could not be read as its packet, and the peer that sent it is why (#897): the read loop closes
/// the connection as a protocol violation, with one Warning that carries neither this exception nor the one it
/// wraps. Thrown where the failure happens, so it is told from a server fault by where it was thrown and not by a
/// type a handler could also throw; anything else a read throws is not the peer's doing and is left to propagate.
/// </summary>
public sealed class PacketReadException : Exception
{
    public PacketReadException(PacketReadFailure failure, NetworkPacketType packetType, Exception innerException)
        : base("A frame's payload could not be read as its packet", innerException)
    {
        Failure = failure;
        PacketType = packetType;
    }

    public PacketReadFailure Failure { get; }

    /// <summary>The opcode the frame's header carried.</summary>
    public NetworkPacketType PacketType { get; }
}
