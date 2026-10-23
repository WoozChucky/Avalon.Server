namespace Avalon.Common;

/// <summary>
/// A stat an item gives: the stored numbers, the retired ones (8 and 9, the old damage stats) never reused. Types a
/// template's stat lines and an instance's rolls (#1179), so it lives beside them in Avalon.Common.
/// </summary>
public enum StatType
{
    Stamina = 0,
    Strength = 1,
    Agility = 2,
    Intellect = 3,
    Armor = 4,
    BlockPct = 5,

    /// <summary>Evasion rating (#1178), formerly the dodge chance.</summary>
    Evasion = 6,
    CritPct = 7,
    Health = 10,
    Power = 11,
    AttackSpeed = 12,
    MovementSpeed = 13,
    Ward = 14,
    FireResistance = 15,
    ColdResistance = 16,
    LightningResistance = 17,
    BlightResistance = 18,

    // The offence model (#1178): percentages of increased damage or healing, then the crit multiplier and the speeds.
    IncreasedDamage = 19,
    IncreasedPhysicalDamage = 20,
    IncreasedFireDamage = 21,
    IncreasedColdDamage = 22,
    IncreasedLightningDamage = 23,
    IncreasedBlightDamage = 24,
    IncreasedAttackDamage = 25,
    IncreasedSpellDamage = 26,
    IncreasedAreaDamage = 27,
    IncreasedProjectileDamage = 28,
    IncreasedMeleeDamage = 29,
    IncreasedDamageOverTime = 30,
    IncreasedHealing = 31,
    CritMultiplier = 32,
    CastSpeed = 33,
    CooldownRecovery = 34,

    // Item modifiers (#1179). Local to the weapon they sit on: added damage by type, a range.
    AddedPhysicalDamage = 35,
    AddedFireDamage = 36,
    AddedColdDamage = 37,
    AddedLightningDamage = 38,
    AddedBlightDamage = 39,

    // Local to the item they sit on: a weapon's physical damage and attack speed, a piece's own defence.
    LocalIncreasedPhysicalDamage = 40,
    LocalIncreasedArmor = 41,
    LocalIncreasedEvasion = 42,
    LocalIncreasedWard = 43,
    LocalAttackSpeed = 44,

    // Global added damage, a range: to every attack, by type, then to every spell, by element.
    AddedPhysicalDamageToAttacks = 45,
    AddedFireDamageToAttacks = 46,
    AddedColdDamageToAttacks = 47,
    AddedLightningDamageToAttacks = 48,
    AddedBlightDamageToAttacks = 49,
    AddedFireDamageToSpells = 50,
    AddedColdDamageToSpells = 51,
    AddedLightningDamageToSpells = 52,
}
