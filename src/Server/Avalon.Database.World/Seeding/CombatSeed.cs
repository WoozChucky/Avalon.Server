using Avalon.Domain.World;
using Avalon.World.Public.Enums;

namespace Avalon.Database.World.Seeding;

/// <summary>
/// The seeded combat area (#506): the formula row and one factors row per class. The model's HasData
/// reads these, and so does a StaticData built without a combat repository (tests). The class factors
/// are exactly the constants CharacterStatsCalculator used before they moved here.
/// </summary>
public static class CombatSeed
{
    public static CombatFormula Formula() => new()
    {
        Id = CombatFormula.SingletonId,
        ArmorBase = 50f,
        ArmorPerLevel = 10f,
        ArmorCap = 0.75f,
        CritMultiplier = 1.5f,
        BlockMultiplier = 0.5f,
        CritCap = 50f,
        DodgeCap = 30f,
        BlockCap = 50f,
    };

    public static ClassStatFactors[] ClassFactors() =>
    [
        new()
        {
            Class = CharacterClass.Warrior, HpPerStamina = 10, PowerPerIntellect = 0, PowerPerAgility = 0, FixedPower = 100,
            AttackPerStrength = 2, AttackPerAgility = 0, AbilityPerIntellect = 0.2,
            BaseBlock = 5.0f, BaseDodge = 3.664f, BaseCrit = 5.0f,
        },
        new()
        {
            Class = CharacterClass.Wizard, HpPerStamina = 5, PowerPerIntellect = 15, PowerPerAgility = 0, FixedPower = null,
            AttackPerStrength = 0.5, AttackPerAgility = 0, AbilityPerIntellect = 3,
            BaseBlock = 0f, BaseDodge = 3.25f, BaseCrit = 1.85f,
        },
        // 0.8 per agility and 2 per intellect: what the old base-power helper's
        // (agility * 0.8) + (intellect * 0.2) * 10 computed, kept as it was (#506 owner decision).
        new()
        {
            Class = CharacterClass.Hunter, HpPerStamina = 8, PowerPerIntellect = 2, PowerPerAgility = 0.8, FixedPower = null,
            AttackPerStrength = 0.5, AttackPerAgility = 1.5, AbilityPerIntellect = 0.5,
            BaseBlock = 0f, BaseDodge = 4.35f, BaseCrit = 5.0f,
        },
        new()
        {
            Class = CharacterClass.Healer, HpPerStamina = 7, PowerPerIntellect = 12, PowerPerAgility = 0, FixedPower = null,
            AttackPerStrength = 0.5, AttackPerAgility = 0, AbilityPerIntellect = 2,
            BaseBlock = 0f, BaseDodge = 3.25f, BaseCrit = 1.85f,
        },
    ];
}
