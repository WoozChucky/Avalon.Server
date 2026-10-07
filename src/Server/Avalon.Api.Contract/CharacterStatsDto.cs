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
    /// <summary>Chance to dodge, in percent.</summary>
    public float DodgePct { get; set; }
    /// <summary>Chance to land a critical hit, in percent.</summary>
    public float CritPct { get; set; }
    public uint AttackDamage { get; set; }
    public uint AbilityDamage { get; set; }
}
