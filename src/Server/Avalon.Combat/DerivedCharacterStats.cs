using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// What a character's class, level and worn gear add up to (spec #463). Written to the entity by
/// CharacterEntity.ApplyStats and saved as the character's CharacterStats row. Combat reads the damage
/// stats, armour, block, evasion and crit, and the main-hand weapon's damage range (#506), from the copy
/// the entity holds, so a hit looks nothing up. The weapon range is not saved, nor are Weapon and Offence (#1178): the
/// main hand's damage and attack time (or the bare hand's), and the offence block with the speeds before their caps.
/// MovementSpeedPct (#627) is the worn gear's MovementSpeed total, in percentage
/// points, before the combat formula's bounds, not saved. MaxWard and Resistances (#1178) are what a full ward
/// reaches and the resistances before any cap; both are saved, the ward's current value is not.
/// </summary>
public readonly record struct DerivedCharacterStats(
    uint MaxHealth,
    uint MaxPower,
    uint Stamina,
    uint Strength,
    uint Agility,
    uint Intellect,
    uint Armor,
    float BlockPct,
    uint Evasion,
    float CritPct,
    uint AttackDamage,
    uint AbilityDamage,
    uint WeaponMin = 0,
    uint WeaponMax = 0,
    float MovementSpeedPct = 0f,
    uint MaxWard = 0,
    Resistances Resistances = default,
    WeaponDamage Weapon = default,
    OffenceStats Offence = default)
{
    /// <summary>The Character-DB row, named by its key alone (no navigation), as every save writes it.</summary>
    public CharacterStats ToRow(CharacterId characterId) => new()
    {
        CharacterId = characterId,
        MaxHealth = MaxHealth,
        MaxPower1 = MaxPower,
        MaxPower2 = 0,
        Stamina = Stamina,
        Strength = Strength,
        Agility = Agility,
        Intellect = Intellect,
        Armor = Armor,
        BlockPct = BlockPct,
        CritPct = CritPct,
        AttackDamage = AttackDamage,
        AbilityDamage = AbilityDamage,
        Evasion = Evasion,
        MaxWard = MaxWard,
        FireResistance = Resistances.Fire,
        ColdResistance = Resistances.Cold,
        LightningResistance = Resistances.Lightning,
        BlightResistance = Resistances.Blight,
    };

    /// <summary>
    /// What a character with these stats attacks with at <paramref name="level" /> (#506). A character's base type is Physical
    /// (#1178): every class ability that deals damage names its own.
    /// </summary>
    public AttackerCombat AttackerAt(ushort level) =>
        new(level, AttackDamage, AbilityDamage, CritPct, WeaponMin, WeaponMax, BaseType: DamageType.Physical);

    /// <summary>What a character with these stats defends with (#506).</summary>
    public DefenderCombat Defence => new(Armor, Evasion, BlockPct, Resistances);
}
