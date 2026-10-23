using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;

namespace Avalon.World.Characters;

/// <summary>
/// What a character's own client is shown of its stats (#506): the derived stats, with the chances made
/// effective by the combat formula's caps, exactly as <see cref="HitResolver" /> clamps them for a roll.
/// The three speeds (#1178) and movement speed (#627) are the values the character's last stats refresh fixed, the ones a
/// cast and the input step use, each already bounded; AttackTimeMs is the main hand's (the bare hand's with none) before
/// attack speed, and CritMultiplier what a crit multiplies by. The evasion rating, the ward's maximum and the resistances
/// (#1178) come from the same refresh, the resistances capped as a hit caps them. WeaponMin and WeaponMax are the range an
/// Attack rolls, the bare hand's with no weapon. Increased is the offence block's increased points, the only part of it
/// the sheet shows. Only what is sent is kept, so a change the client is not shown sends nothing. A value type, so the
/// last one sent can be compared with the current one without allocating.
/// </summary>
public readonly record struct CharacterSheet(
    uint Stamina,
    uint Strength,
    uint Agility,
    uint Intellect,
    uint Armor,
    float CritPct,
    float BlockPct,
    uint WeaponMin,
    uint WeaponMax,
    float MovementSpeed,
    uint Evasion,
    float EvasionPct,
    uint MaxWard,
    float FireResistance,
    float ColdResistance,
    float LightningResistance,
    float BlightResistance,
    float CritMultiplier,
    float AttackSpeedPct,
    float CastSpeedPct,
    float CooldownRecoveryPct,
    uint AttackTimeMs,
    IncreasedPoints Increased)
{
    // The one array every sheet's increased points are written into: a sheet is encoded on the tick thread as soon as it
    // is built (SCharacterStatsPacket.Create), so the packet never outlives the next sheet. Per thread, for the tests.
#pragma warning disable IDE1006
    [ThreadStatic] private static float[]? t_increased;
#pragma warning restore IDE1006

    /// <summary>
    /// <paramref name="level" /> is the character's own: EvasionPct carries the chance to evade an attacker of that level
    /// (#1178), in percentage points. No source raises the maximum resistance yet, so each resistance is capped at the
    /// formula's ResistanceCap. <paramref name="pace" /> is the character's, bounded by the formula its refresh read.
    /// </summary>
    public static CharacterSheet From(in DerivedCharacterStats s, ushort level, CombatFormula formula, in CasterPace pace,
        float movementSpeed) => new(
        s.Stamina, s.Strength, s.Agility, s.Intellect, s.Armor,
        HitResolver.EffectivePct(s.CritPct, formula.CritCap),
        HitResolver.EffectivePct(s.BlockPct, formula.BlockCap),
        s.Weapon.Min1, s.Weapon.Max1, movementSpeed,
        s.Evasion,
        (float)(HitResolver.EvasionChance(s.Evasion, level, formula) * 100d),
        s.MaxWard,
        HitResolver.EffectiveResistance(s.Resistances.Fire, 0f, formula),
        HitResolver.EffectiveResistance(s.Resistances.Cold, 0f, formula),
        HitResolver.EffectiveResistance(s.Resistances.Lightning, 0f, formula),
        HitResolver.EffectiveResistance(s.Resistances.Blight, 0f, formula),
        (float)HitResolver.CritMultiplier(s.Offence.CritMultiplierBonus, formula),
        pace.AttackSpeedPct, pace.CastSpeedPct, pace.CooldownRecoveryPct,
        s.Weapon.AttackTimeMs,
        IncreasedPoints.Of(s.Offence));

    /// <summary>
    /// The packet, to be encoded before the next sheet is built: its increased points are the calling thread's one array,
    /// which the next sheet overwrites.
    /// </summary>
    public SCharacterStatsPacket ToPacket()
    {
        float[] increased = t_increased ??= new float[OffenceStats.Count];
        Increased.CopyTo(increased);

        return new SCharacterStatsPacket
        {
            Stamina = Stamina,
            Strength = Strength,
            Agility = Agility,
            Intellect = Intellect,
            Armor = Armor,
            CritPct = CritPct,
            BlockPct = BlockPct,
            WeaponMin = WeaponMin,
            WeaponMax = WeaponMax,
            MovementSpeed = MovementSpeed,
            Evasion = Evasion,
            EvasionPct = EvasionPct,
            MaxWard = MaxWard,
            FireResistance = FireResistance,
            ColdResistance = ColdResistance,
            LightningResistance = LightningResistance,
            BlightResistance = BlightResistance,
            CritMultiplier = CritMultiplier,
            AttackSpeedPct = AttackSpeedPct,
            CastSpeedPct = CastSpeedPct,
            CooldownRecoveryPct = CooldownRecoveryPct,
            AttackTimeMs = AttackTimeMs,
            IncreasedPct = increased,
        };
    }
}
