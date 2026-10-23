namespace Avalon.Common;

/// <summary>
/// How rare an item is (#1179). A template is Common or Unique; Magic and Rare exist only on an instance's rolls. Lives
/// in Avalon.Common because instance data, the wire and the modding surface all name it.
/// </summary>
public enum ItemRarity : byte
{
    /// <summary>No modifiers.</summary>
    Common = 0,

    /// <summary>One or two modifiers.</summary>
    Magic = 1,

    /// <summary>Three to six modifiers.</summary>
    Rare = 2,

    /// <summary>Hand-made: fixed stats whose values each copy rolls.</summary>
    Unique = 3,
}
