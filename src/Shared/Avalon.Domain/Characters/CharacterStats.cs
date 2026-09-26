using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

public class CharacterStats
{
    [Required]
    public Character Character { get; set; }
    public CharacterId CharacterId { get; set; }
    public uint MaxHealth { get; set; }
    public uint MaxPower1 { get; set; }
    public uint MaxPower2 { get; set; }
    public uint Stamina { get; set; }
    public uint Strength { get; set; }
    public uint Agility { get; set; }
    public uint Intellect { get; set; }
    public uint Armor { get; set; }
    public float BlockPct { get; set; }
    public float DodgePct { get; set; }
    public float CritPct { get; set; }
    public uint AttackDamage { get; set; }
    public uint AbilityDamage { get; set; }
}
