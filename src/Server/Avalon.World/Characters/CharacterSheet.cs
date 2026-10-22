using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;

namespace Avalon.World.Characters;

/// <summary>
/// What a character's own client is shown of its stats (#506): the derived stats, with the chances made
/// effective by the combat formula's caps, exactly as <see cref="HitResolver" /> clamps them for a roll.
/// Haste and movement speed (#627) are the values the character's last stats refresh fixed, the ones a
/// cast and the input step use. A value type, so the last one sent can be compared with the current one
/// without allocating.
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
    uint WeaponMax,
    float HastePct,
    float MovementSpeed)
{
    /// <summary>
    /// <paramref name="level" /> is the character's own: DodgePct carries the chance to evade an attacker of that level
    /// (#1178), in percentage points.
    /// </summary>
    public static CharacterSheet From(in DerivedCharacterStats s, ushort level, CombatFormula formula, float hastePct,
        float movementSpeed) => new(
        s.Stamina, s.Strength, s.Agility, s.Intellect, s.Armor, s.AttackDamage, s.AbilityDamage,
        HitResolver.EffectivePct(s.CritPct, formula.CritCap),
        (float)(HitResolver.EvasionChance(s.Evasion, level, formula) * 100d),
        HitResolver.EffectivePct(s.BlockPct, formula.BlockCap),
        s.WeaponMin, s.WeaponMax, hastePct, movementSpeed);

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
        DodgePct = DodgePct,
        BlockPct = BlockPct,
        WeaponMin = WeaponMin,
        WeaponMax = WeaponMax,
        HastePct = HastePct,
        MovementSpeed = MovementSpeed,
    };
}
