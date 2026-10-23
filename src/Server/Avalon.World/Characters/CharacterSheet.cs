using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;

namespace Avalon.World.Characters;

/// <summary>
/// What a character's own client is shown of its stats (#506): the derived stats, with the chances made
/// effective by the combat formula's caps, exactly as <see cref="HitResolver" /> clamps them for a roll.
/// HastePct carries the effective attack speed (#1178) until the sheet carries all three speeds; it and movement speed
/// (#627) are the values the character's last stats refresh fixed, the ones a cast and the input step use. The evasion
/// rating, the ward's maximum and the resistances (#1178) come from the same refresh, the resistances capped as a hit caps
/// them. AttackDamage and AbilityDamage are always 0 (#1178): the old damage stats are gone, and the wire drops them with
/// the offence block's fields. WeaponMin and WeaponMax are the range an Attack rolls, the bare hand's with no weapon.
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
    float BlockPct,
    uint WeaponMin,
    uint WeaponMax,
    float HastePct,
    float MovementSpeed,
    uint Evasion,
    float EvasionPct,
    uint MaxWard,
    float FireResistance,
    float ColdResistance,
    float LightningResistance,
    float BlightResistance)
{
    /// <summary>
    /// <paramref name="level" /> is the character's own: EvasionPct carries the chance to evade an attacker of that level
    /// (#1178), in percentage points. No source raises the maximum resistance yet, so each resistance is capped at the
    /// formula's ResistanceCap.
    /// </summary>
    public static CharacterSheet From(in DerivedCharacterStats s, ushort level, CombatFormula formula, float hastePct,
        float movementSpeed) => new(
        s.Stamina, s.Strength, s.Agility, s.Intellect, s.Armor, 0u, 0u,
        HitResolver.EffectivePct(s.CritPct, formula.CritCap),
        HitResolver.EffectivePct(s.BlockPct, formula.BlockCap),
        s.Weapon.Min1, s.Weapon.Max1, hastePct, movementSpeed,
        s.Evasion,
        (float)(HitResolver.EvasionChance(s.Evasion, level, formula) * 100d),
        s.MaxWard,
        HitResolver.EffectiveResistance(s.Resistances.Fire, 0f, formula),
        HitResolver.EffectiveResistance(s.Resistances.Cold, 0f, formula),
        HitResolver.EffectiveResistance(s.Resistances.Lightning, 0f, formula),
        HitResolver.EffectiveResistance(s.Resistances.Blight, 0f, formula));

    public SCharacterStatsPacket ToPacket() => new()
    {
        Stamina = Stamina,
        Strength = Strength,
        Agility = Agility,
        Intellect = Intellect,
        Armor = Armor,
        AttackDamage = AttackDamage,
        AbilityDamage = AbilityDamage,
        CritPct = CritPct,
        BlockPct = BlockPct,
        WeaponMin = WeaponMin,
        WeaponMax = WeaponMax,
        HastePct = HastePct,
        MovementSpeed = MovementSpeed,
        Evasion = Evasion,
        EvasionPct = EvasionPct,
        MaxWard = MaxWard,
        FireResistance = FireResistance,
        ColdResistance = ColdResistance,
        LightningResistance = LightningResistance,
        BlightResistance = BlightResistance,
    };
}
