namespace Avalon.Network.Packets.Character;

/// <summary>
/// Why an imprint request (#1180) was or was not applied. Anything but Ok, Bricked and InternalError changed nothing.
/// Append-only.
/// </summary>
public enum ItemImprintResult : byte
{
    /// <summary>
    /// The glyph and the gold are spent, and the item carries the glyph's affix and is bound (Attuned). The item and the
    /// gold arrive in the tick's SInventoryUpdatePacket.
    /// </summary>
    Ok = 0,

    Dead = 1,

    /// <summary>The character is stunned: it imprints nothing until the stun ends.</summary>
    Stunned = 2,

    /// <summary>No Resonance Workbench of that guid here: not in this instance, despawned, or a doodad of another kind.</summary>
    NotFound = 3,

    /// <summary>The character is beyond the Workbench's interaction range.</summary>
    TooFar = 4,

    /// <summary>
    /// The glyph slot holds no usable glyph: it is empty or past the Bag, its item is not a glyph, or the glyph's affix is
    /// no longer one the world knows.
    /// </summary>
    NoGlyph = 5,

    /// <summary>The target slot is empty, or past the Bag.</summary>
    NoItem = 6,

    /// <summary>The item is offered in a trade: nothing is imprinted onto it until it leaves the trade.</summary>
    Locked = 7,

    /// <summary>The item is not identified: its affixes are not known yet.</summary>
    Unidentified = 8,

    /// <summary>The item is not a magic or rare copy of a base: a common or unique copy, or a glyph.</summary>
    WrongRarity = 9,

    /// <summary>The item is Tainted: it is never modified again.</summary>
    Tainted = 10,

    /// <summary>
    /// The item has no free slot of the glyph's kind: its prefixes, or its suffixes, sealed ones included, already fill
    /// what its rarity allows (a magic item one of each, a rare three).
    /// </summary>
    NoRoom = 11,

    /// <summary>The item already carries an affix of the glyph affix's family.</summary>
    GroupTaken = 12,

    /// <summary>The glyph's affix never rolls on this kind of item.</summary>
    WrongSlot = 13,

    /// <summary>The item's level is below the glyph affix's required item level.</summary>
    ItemLevelTooLow = 14,

    /// <summary>The character cannot spend the imprint's cost (gold held for a trade cannot be spent).</summary>
    NotEnoughGold = 15,

    /// <summary>
    /// A success: the glyph and the gold are spent and the affix imprinted, but the item broke under it. Its affixes are
    /// rerolled among their weakest tiers, and it is Tainted, never modified or bound again. The item arrives in the
    /// tick's SInventoryUpdatePacket.
    /// </summary>
    Bricked = 16,

    /// <summary>
    /// Something on the server failed after the request was allowed; it is logged, and nothing changed: the glyph and the
    /// item are left as they were, and any gold spent on the imprint is given back.
    /// </summary>
    InternalError = 17,
}
