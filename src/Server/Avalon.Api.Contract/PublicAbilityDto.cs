using System.Text.Json.Serialization;
using Avalon.World.Public.Enums;

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
    /// <summary>None when the ability deals or heals no direct amount; the fields below then mean nothing.</summary>
    public AbilityAmountKind AmountKind { get; set; }
    public uint EffectValue { get; set; }
    public AbilityScalingStat ScalingStat { get; set; }
    public float ScalingCoefficient { get; set; }
    public float BaseDamageCoefficient { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AbilityAmountKind { None, Damage, Healing }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AbilityScalingStat { Attack, Ability }
