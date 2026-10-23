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
    public float CritPct { get; set; }

    /// <summary>The evasion rating (#1178), a flat number weighed against an attacker's level.</summary>
    public uint Evasion { get; set; }

    /// <summary>The ward a full recharge reaches (#1178).</summary>
    public uint MaxWard { get; set; }

    /// <summary>The fire resistance (#1178), in percent, before any cap.</summary>
    public float FireResistance { get; set; }

    /// <summary>The cold resistance (#1178), in percent, before any cap.</summary>
    public float ColdResistance { get; set; }

    /// <summary>The lightning resistance (#1178), in percent, before any cap.</summary>
    public float LightningResistance { get; set; }

    /// <summary>The blight resistance (#1178), in percent, before any cap.</summary>
    public float BlightResistance { get; set; }
}
