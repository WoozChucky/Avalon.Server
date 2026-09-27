using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Combat;

namespace Avalon.World.Characters;

/// <summary>
/// What a character's own client is shown of its stats (#506): the derived stats, with the chances made
/// effective by the combat formula's caps, exactly as <see cref="HitResolver" /> clamps them for a roll.
/// A value type, so the last one sent can be compared with the current one without allocating.
/// </summary>
public readonly record struct CharacterSheet(
    uint Stamina,
    uint Strength,
    uint Agility,
    uint Intellect,
    uint Armor,
    uint AttackDamage,
    uint AbilityDamage,
    float CritPct,
    float DodgePct,
    float BlockPct,
    uint WeaponMin,
    uint WeaponMax)
{
    public static CharacterSheet From(in DerivedCharacterStats s, CombatFormula formula) => new(
        s.Stamina, s.Strength, s.Agility, s.Intellect, s.Armor, s.AttackDamage, s.AbilityDamage,
        HitResolver.EffectivePct(s.CritPct, formula.CritCap),
        HitResolver.EffectivePct(s.DodgePct, formula.DodgeCap),
        HitResolver.EffectivePct(s.BlockPct, formula.BlockCap),
        s.WeaponMin, s.WeaponMax);

    public SCharacterStatsPacket ToPacket() => new()
    {
        Stamina = Stamina, Strength = Strength, Agility = Agility, Intellect = Intellect, Armor = Armor,
        AttackDamage = AttackDamage, AbilityDamage = AbilityDamage,
        CritPct = CritPct, DodgePct = DodgePct, BlockPct = BlockPct,
        WeaponMin = WeaponMin, WeaponMax = WeaponMax,
    };
}
