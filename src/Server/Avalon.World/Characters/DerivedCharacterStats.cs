using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;

namespace Avalon.World.Characters;

/// <summary>
/// What a character's class, level and worn gear add up to (spec #463). Written to the entity by
/// CharacterEntity.ApplyStats and saved as the character's CharacterStats row. Combat reads none of
/// the attributes, armour or damage values yet; they are stored for the combat work that will.
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
    uint AbilityDamage)
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
}
