namespace Avalon.Network.Packets.Character;

/// <summary>
/// Why an identify request (#1179) was or was not applied. Anything but Ok and InternalError changed nothing.
/// Append-only.
/// </summary>
public enum ItemIdentifyResult : byte
{
    /// <summary>Identified, and paid for. The revealed item arrives in the tick's SInventoryUpdatePacket.</summary>
    Ok = 0,

    Dead = 1,

    /// <summary>The character is stunned: it identifies nothing until the stun ends.</summary>
    Stunned = 2,

    /// <summary>No Resonance Workbench of that guid here: not in this instance, despawned, or a doodad of another kind.</summary>
    NotFound = 3,

    /// <summary>The character is beyond the Workbench's interaction range.</summary>
    TooFar = 4,

    /// <summary>The Bag slot is empty, or past the Bag.</summary>
    NoItem = 5,

    AlreadyIdentified = 6,

    /// <summary>The item is offered in a trade: it is not identified until it leaves the trade.</summary>
    Locked = 7,

    /// <summary>The gold the character can spend (its balance less any gold held for a trade) is below the cost.</summary>
    NotEnoughGold = 8,

    /// <summary>
    /// Something on the server failed after the request was allowed; it is logged. The item may not be identified, and
    /// a cost above 0 may already have been spent: the gold is taken first and is not given back.
    /// </summary>
    InternalError = 9,
}
