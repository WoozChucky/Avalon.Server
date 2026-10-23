using Avalon.Common;
using Avalon.Domain.World;

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
        BlockCap = 50f,
        // #627: gear moves a character between half and 1.35 times the base
        // 4 m/s. The cap is owner-set to keep a whole change from the base under the client's snap:
        // 4 x 0.35 x 0.1 s = 0.14 m of drift over a round trip, below 0.15 m.
        MoveSpeedCap = 35f,
        MoveSpeedFloor = -50f,
        // #1178: evasion has armour's shape. These constants keep each class's former base dodge as evasion from Agility
        // against a creature of its level, at levels 1 and 10, within half a point; 1.125 is exact in binary, so a
        // product such as 20 x 1.125 never truncates. Resistances cap at 75 % and floor at -100 %, which at most doubles
        // an element. Strength and Intellect give a little armour and ward, for every class.
        EvasionBase = 560f,
        EvasionPerLevel = 40f,
        EvasionCap = 30f,
        ResistanceCap = 75f,
        ResistanceFloor = -100f,
        ArmorPerStrength = 0.1f,
        EvasionPerAgility = 1.125f,
        WardPerIntellect = 0.25f,
        // #1178, offence: 3.5 % per point of Strength (physical attacks) and of Intellect (spells, healing) keeps a point's
        // marginal worth near today's; 5 life per Stamina is the largest rate that keeps the Wizard's base health 0 or
        // more. Every speed counts up to 50 %. The bare hand rolls 3-6 every 816 ms, which carries today's bare hits and
        // basic pace over with a level-1 Warrior's Agility.
        IncreasedPhysicalAttackPerStrength = 3.5f,
        AttackSpeedPerAgility = 0.1f,
        IncreasedSpellPerIntellect = 3.5f,
        IncreasedHealingPerIntellect = 3.5f,
        LifePerStamina = 5,
        BaseCritPct = 5f,
        AttackSpeedCap = 50f,
        CastSpeedCap = 50f,
        CooldownRecoveryCap = 50f,
        UnarmedDamageMin = 3,
        UnarmedDamageMax = 6,
        UnarmedAttackTimeMs = 816,
    };

    public static ClassStatFactors[] ClassFactors() =>
    [
        new()
        {
            Class = CharacterClass.Warrior, PowerPerIntellect = 0, PowerPerAgility = 0, FixedPower = 100,
        },
        new()
        {
            Class = CharacterClass.Wizard, PowerPerIntellect = 15, PowerPerAgility = 0, FixedPower = null,
        },
        // 0.8 per agility and 2 per intellect: what the old base-power helper's
        // (agility * 0.8) + (intellect * 0.2) * 10 computed, kept as it was (#506 owner decision).
        new()
        {
            Class = CharacterClass.Hunter, PowerPerIntellect = 2, PowerPerAgility = 0.8, FixedPower = null,
        },
        new()
        {
            Class = CharacterClass.Cleric, PowerPerIntellect = 12, PowerPerAgility = 0, FixedPower = null,
        },
    ];
}
