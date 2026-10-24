namespace Avalon.Domain.World;

/// <summary>
/// What a catalog modifier group is (#1179): a prefix or a suffix an item may roll, a unique's own modifier, or a base's
/// implicit stats. Only prefixes and suffixes count toward an item's affix limits and name it.
/// </summary>
public enum ItemModifierKind : byte
{
    Prefix = 1,
    Suffix = 2,
    Unique = 3,
    Implicit = 4,
}
