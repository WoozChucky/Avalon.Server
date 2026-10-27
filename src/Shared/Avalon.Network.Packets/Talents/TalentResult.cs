namespace Avalon.Network.Packets.Talents;

/// <summary>
/// Why a talent request (#1181) was or was not applied. Anything but Ok and InternalError changed nothing. Append-only
/// from its first release.
/// </summary>
public enum TalentResult : byte
{
    /// <summary>Every node named is held. The state follows in an STalentStatePacket.</summary>
    Ok = 0,

    Dead = 1,

    /// <summary>More nodes than the points the character has left to spend, or than the tree holds.</summary>
    TooMany = 2,

    /// <summary>A node the tree does not hold.</summary>
    UnknownNode = 3,

    /// <summary>A node already held, listed twice in the request, or a class start, which is never allocated.</summary>
    AlreadyAllocated = 4,

    /// <summary>A node with no neighbour that is the class's start, a held node or a node named earlier in the request.</summary>
    NotConnected = 5,

    /// <summary>A node whose fork partner is held, or named earlier in the request.</summary>
    ForkLocked = 6,

    /// <summary>Something on the server failed after the request was allowed; it is logged.</summary>
    InternalError = 7,
}
