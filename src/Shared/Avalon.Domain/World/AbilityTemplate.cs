using System.ComponentModel.DataAnnotations;
using Avalon.Common.ValueObjects;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.Domain.World;

public class AbilityTemplate : IDbEntity<AbilityId>
{
    [Key]
    public AbilityId Id { get; set; }

    public string Name { get; set; }

    public uint CastTime { get; set; } // in milliseconds

    public uint Cooldown { get; set; } // in milliseconds

    public uint Cost { get; set; } // in power points

    public string SpellScript { get; set; }

    public SpellRange Range { get; set; } // in meters

    public SpellEffect Effects { get; set; }

    public uint EffectValue { get; set; }

    public List<CharacterClass> AllowedClasses { get; set; } = []; // Default to no classes

    [Required] public float ThreatMultiplier { get; set; } = 1.0f;

    [Required] public float HealThreatPerHp { get; set; } = 0.0f;

    [Required] public uint TauntDurationMs { get; set; } = 0;

    [Required] public AbilityFlags Flags { get; set; } = AbilityFlags.None;

    [Required] public uint AnimationId { get; set; } = 0;
}
