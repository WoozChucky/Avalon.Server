namespace Avalon.Api.Contract;

/// <summary>
/// A character's stats as the world server last saved them (#676): recalculated from class, level and
/// worn gear, so they change only when the character is saved.
/// </summary>
public sealed class CharacterStatsDto
{
    public uint CharacterId { get; set; }
    public uint MaxHealth { get; set; }
    public uint MaxPower1 { get; set; }
    public uint MaxPower2 { get; set; }
    public uint Stamina { get; set; }
    public uint Strength { get; set; }
    public uint Agility { get; set; }
    public uint Intellect { get; set; }
    public uint Armor { get; set; }
    /// <summary>Chance to block, in percent (5 is 5%).</summary>
    public float BlockPct { get; set; }
    /// <summary>Chance to land a critical hit, in percent.</summary>
    public float CritPct { get; set; }
    public uint AttackDamage { get; set; }
    public uint AbilityDamage { get; set; }
    /// <summary>Evasion rating: the chance to evade it gives depends on the attacker's level.</summary>
    public uint Evasion { get; set; }
    /// <summary>The ward a full recharge reaches.</summary>
    public uint MaxWard { get; set; }
    /// <summary>Fire resistance, in percent, before the cap a hit applies.</summary>
    public float FireResistance { get; set; }
    /// <summary>Cold resistance, in percent, before the cap a hit applies.</summary>
    public float ColdResistance { get; set; }
    /// <summary>Lightning resistance, in percent, before the cap a hit applies.</summary>
    public float LightningResistance { get; set; }
    /// <summary>Blight resistance, in percent, before the cap a hit applies.</summary>
    public float BlightResistance { get; set; }
}
