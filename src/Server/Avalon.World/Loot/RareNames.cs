using Avalon.Domain.World;

namespace Avalon.World.Loot;

/// <summary>
/// The two words of a rare item's name (#1179, B-R19), drawn once when it rolls and stored on the copy, so editing these
/// lists never renames an item a player holds. The first word is any of one list; the second follows the item's kind.
/// </summary>
public static class RareNames
{
    private static readonly string[] s_first =
        ["Thorn", "Moss", "Ash", "Fen", "Bramble", "Grim", "Dusk", "Root", "Bark", "Hollow", "Ember", "Blight", "Gloam", "Wild", "Stag", "Mire"];

    private static readonly string[] s_weapons = ["Bite", "Fang", "Edge", "Song", "Spine", "Sting"];
    private static readonly string[] s_armour = ["Shell", "Hide", "Ward", "Bark", "Mantle", "Coat"];
    private static readonly string[] s_jewellery = ["Eye", "Heart", "Knot", "Coil", "Seed", "Tear"];

    /// <summary>A rare <paramref name="kind" />'s name: two draws, the first word and then the second.</summary>
    public static string Draw(ItemBaseKind kind, ILootRandom random)
    {
        string first = s_first[random.NextInt64(0, s_first.Length)];
        string[] seconds = SecondWords(kind);
        return string.Concat(first, " ", seconds[random.NextInt64(0, seconds.Length)]);
    }

    // Weapons; armour pieces and shields; amulets, rings and caster off-hands. A trinket (only ever a unique, so never
    // rare) is jewellery.
    private static string[] SecondWords(ItemBaseKind kind) => kind switch
    {
        ItemBaseKind.Sword or ItemBaseKind.Mace or ItemBaseKind.Staff or ItemBaseKind.Bow => s_weapons,
        ItemBaseKind.Amulet or ItemBaseKind.Ring or ItemBaseKind.CasterOffHand or ItemBaseKind.Trinket => s_jewellery,
        _ => s_armour,
    };
}
