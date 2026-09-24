namespace Avalon.World.Public.Enums;

/// <summary>
/// How dangerous a creature template is, which scales its derived stats through
/// <c>CreatureRarityModifiers</c>. Per template rather than rolled per spawn: a Bramblemaw Alpha is
/// always an Elite.
/// </summary>
public enum CreatureRarity : ushort
{
    Normal = 0,
    Elite = 1,
    Rare = 2,
    Boss = 3
}
