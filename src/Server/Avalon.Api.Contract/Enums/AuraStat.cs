using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>A stat an aura modifies (auras): the stored numbers, a retired one (7, haste) never reused.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuraStat
{
    Armor = 1,
    AttackDamage = 2,
    AbilityDamage = 3,
    CritPct = 4,

    /// <summary>Evasion rating (#1178), formerly the dodge chance.</summary>
    Evasion = 5,
    BlockPct = 6,
    MovementSpeed = 8,
    MaxHealth = 9,
    MaxPower = 10,
    FireResistance = 11,
    ColdResistance = 12,
    LightningResistance = 13,
    BlightResistance = 14,
    MaxWard = 15,

    // The offence model (#1178): percentages of increased damage or healing, then the crit multiplier and the speeds.
    IncreasedDamage = 16,
    IncreasedPhysicalDamage = 17,
    IncreasedFireDamage = 18,
    IncreasedColdDamage = 19,
    IncreasedLightningDamage = 20,
    IncreasedBlightDamage = 21,
    IncreasedAttackDamage = 22,
    IncreasedSpellDamage = 23,
    IncreasedAreaDamage = 24,
    IncreasedProjectileDamage = 25,
    IncreasedMeleeDamage = 26,
    IncreasedDamageOverTime = 27,
    IncreasedHealing = 28,
    CritMultiplier = 29,
    AttackSpeed = 30,
    CastSpeed = 31,
    CooldownRecovery = 32,
}
