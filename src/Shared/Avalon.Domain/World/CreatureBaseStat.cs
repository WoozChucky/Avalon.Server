namespace Avalon.Domain.World;

/// <summary>
/// Base creature stats for one level, before the template's modifiers and its rarity multipliers.
/// Deliberately shaped like <see cref="ClassLevelStat" />: tuning lives in the database, not in code.
/// </summary>
/// <remarks>
/// Mana is absent on purpose: creatures spawn with no power and cannot cast, so it would be authored
/// data that nothing reads.
/// </remarks>
public class CreatureBaseStat
{
    public ushort Level { get; set; }
    public uint Health { get; set; }
    public uint DamageMin { get; set; }
    public uint DamageMax { get; set; }
    public uint Experience { get; set; }

    /// <summary>
    /// Armour at this level (#506), before the template's ArmorModifier; it reduces the hits the creature
    /// takes. Reloaded with the creatures area.
    /// </summary>
    public uint Armor { get; set; }
}
