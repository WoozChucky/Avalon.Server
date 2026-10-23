using System.Text.Json.Serialization;
using Avalon.Common;

namespace Avalon.Api.Contract;

/// <summary>An ability as a tooltip shows it to anyone: no script, threat, shape or projectile fields.</summary>
public sealed class PublicAbilityDto
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public uint Cost { get; set; }
    public PowerType CostPowerType { get; set; }
    public SpellRange Range { get; set; }
    /// <summary>In milliseconds.</summary>
    public uint CastTime { get; set; }
    /// <summary>In milliseconds.</summary>
    public uint Cooldown { get; set; }
    public List<CharacterClass> AllowedClasses { get; set; } = [];
    /// <summary>The type of damage the ability deals, or null when it names none (#1178).</summary>
    public DamageType? DamageType { get; set; }
    /// <summary>None when the ability deals or heals no direct amount.</summary>
    public AbilityAmountKind AmountKind { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AbilityAmountKind { None, Damage, Healing }
