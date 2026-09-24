namespace Avalon.Domain.World;

/// <summary>
/// Base creature stats for one level, before the template's modifiers and its rarity multipliers.
/// Deliberately shaped like <see cref="ClassLevelStat" />: tuning lives in the database, not in code.
/// </summary>
/// <remarks>
/// Armour and mana are absent on purpose. <c>CombatService.ApplyDamage</c> applies raw damage with no
/// mitigation step anywhere, and creatures spawn with no power and cannot cast, so both would be
/// authored data that nothing reads.
/// </remarks>
public class CreatureBaseStat
{
    public ushort Level { get; set; }
    public uint Health { get; set; }
    public uint DamageMin { get; set; }
    public uint DamageMax { get; set; }
    public uint Experience { get; set; }
}
