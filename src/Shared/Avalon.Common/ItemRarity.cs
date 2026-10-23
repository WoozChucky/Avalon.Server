namespace Avalon.Common;

/// <summary>
/// How rare an item is (#1179). A template is Common or Unique; an instance carries its own rarity, which Magic and
/// Rare exist only on. Lives in Avalon.Common beside <see cref="ItemInstanceFlags" /> because, like the flags, it is
/// a fact of every instance that the instance row, the wire and the modding surface's InventoryItem all carry.
/// </summary>
public enum ItemRarity : byte
{
    /// <summary>No modifiers.</summary>
    Common = 0,

    /// <summary>One or two modifiers.</summary>
    Magic = 1,

    /// <summary>Three to six modifiers.</summary>
    Rare = 2,

    /// <summary>Hand-made: its stats are its unique modifier, whose values each copy rolls.</summary>
    Unique = 3,
}
