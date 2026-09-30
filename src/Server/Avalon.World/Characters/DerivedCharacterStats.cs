using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.World.Combat;

namespace Avalon.World.Characters;

/// <summary>
/// What a character's class, level and worn gear add up to (spec #463). Written to the entity by
/// CharacterEntity.ApplyStats and saved as the character's CharacterStats row. Combat reads the damage
/// stats, armour, block, dodge and crit, and the main-hand weapon's damage range (#506), from the copy
/// the entity holds, so a hit looks nothing up. The weapon range is not saved. HastePct and
/// MovementSpeedPct (#627) are the worn gear's AttackSpeed and MovementSpeed totals, in percentage
/// points, before the combat formula's bounds; neither is saved.
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
    float DodgePct,
    float CritPct,
    uint AttackDamage,
    uint AbilityDamage,
    uint WeaponMin = 0,
    uint WeaponMax = 0,
    float HastePct = 0f,
    float MovementSpeedPct = 0f)
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
        DodgePct = DodgePct,
        CritPct = CritPct,
        AttackDamage = AttackDamage,
        AbilityDamage = AbilityDamage,
    };

    /// <summary>What a character with these stats attacks with at <paramref name="level" /> (#506).</summary>
    public AttackerCombat AttackerAt(ushort level) =>
        new(level, AttackDamage, AbilityDamage, CritPct, WeaponMin, WeaponMax);

    /// <summary>What a character with these stats defends with (#506).</summary>
    public DefenderCombat Defence => new(Armor, DodgePct, BlockPct);

    /// <summary>The haste that counts (#627): the gear total, 0 to the formula's HasteCap.</summary>
    public float EffectiveHastePct(Avalon.Domain.World.CombatFormula formula) =>
        Math.Clamp(HastePct, 0f, Math.Max(0f, formula.HasteCap));
}
