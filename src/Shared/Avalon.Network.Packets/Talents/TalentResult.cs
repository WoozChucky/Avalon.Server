namespace Avalon.Network.Packets.Talents;

/// <summary>
/// Why a talent request (#1181) was or was not applied. Anything but Ok and InternalError changed nothing. Append-only
/// from its first release.
/// </summary>
public enum TalentResult : byte
{
    /// <summary>
    /// Every node named is held after an allocation, or no longer held after a refund. An accepted non-empty request is
    /// followed by an STalentStatePacket.
    /// </summary>
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

    /// <summary>The character is stunned.</summary>
    Stunned = 8,

    /// <summary>No Lectern of that guid in the character's instance.</summary>
    NotFound = 9,

    /// <summary>The Lectern is out of the character's reach.</summary>
    TooFar = 10,

    /// <summary>The character is casting an ability.</summary>
    Casting = 11,

    /// <summary>A node to refund that is not held, listed twice in the request, or a class start, which is never held.</summary>
    NotAllocated = 12,

    /// <summary>The refund would cut a held node off from the class's start; the node named is the lowest one cut off.</summary>
    WouldDisconnect = 13,

    /// <summary>The refund costs more gold than the character can spend, gold an open trade holds left out.</summary>
    NotEnoughGold = 14,
}
