namespace Avalon.World.Public.Enums;

[Flags]
public enum SpellEffect : ushort
{
    None = 0,
    Damage = 1,
    Heal = 2,
    Buff = 4,
    Debuff = 8,
    Utility = 16
}
