namespace Avalon.Network.Packets.Character;

/// <summary>
/// Why an extract request (#1180) was or was not applied. Anything but Ok and InternalError changed nothing.
/// Append-only.
/// </summary>
public enum ItemExtractResult : byte
{
    /// <summary>
    /// The item is destroyed and a bound glyph holding the affix is in its Bag slot. Both arrive in the tick's
    /// SInventoryUpdatePacket.
    /// </summary>
    Ok = 0,

    Dead = 1,

    /// <summary>The character is stunned: it extracts nothing until the stun ends.</summary>
    Stunned = 2,

    /// <summary>No Resonance Workbench of that guid here: not in this instance, despawned, or a doodad of another kind.</summary>
    NotFound = 3,

    /// <summary>The character is beyond the Workbench's interaction range.</summary>
    TooFar = 4,

    /// <summary>The Bag slot is empty, or past the Bag.</summary>
    NoItem = 5,

    /// <summary>The item is not identified: its affixes are not known yet.</summary>
    Unidentified = 6,

    /// <summary>The item is offered in a trade: nothing is extracted from it until it leaves the trade.</summary>
    Locked = 7,

    /// <summary>The item is Tainted: it is never modified again.</summary>
    Tainted = 8,

    /// <summary>
    /// Nothing can be extracted here: the item is not a magic or rare copy of a base (a common or unique copy, a glyph),
    /// it can never be destroyed, it carries no row of that modifier, or that row is an implicit, a unique's own or a
    /// sealed affix; or the world holds no glyph template.
    /// </summary>
    NotExtractable = 9,

    /// <summary>
    /// Something on the server failed after the request was allowed; it is logged. The item is left as it was: the glyph
    /// is built in full before the item is destroyed.
    /// </summary>
    InternalError = 10,
}
