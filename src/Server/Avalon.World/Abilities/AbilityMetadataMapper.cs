using Avalon.Domain.World;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Abilities;

/// <summary>The one place a template becomes runtime metadata. Milliseconds in, seconds out.</summary>
public static class AbilityMetadataMapper
{
    public static AbilityMetadata From(AbilityTemplate template) => new()
    {
        Name = template.Name,
        Cooldown = (float)template.Cooldown / 1000,
        CastTime = (float)template.CastTime / 1000,
        Cost = template.Cost,
        Range = template.Range,
        Effects = template.Effects,
        EffectValue = template.EffectValue,
        ScriptName = template.SpellScript,
        ThreatMultiplier = template.ThreatMultiplier,
        HealThreatPerHp = template.HealThreatPerHp,
        TauntDurationMs = template.TauntDurationMs,
        Flags = template.Flags,
        AnimationId = template.AnimationId,
        AimMode = template.AimMode,
        Shape = template.Shape,
        Anchor = template.Anchor,
        Reach = template.Reach,
        Radius = template.Radius,
        ArcDegrees = template.ArcDegrees,
        ProjectileSpeed = template.ProjectileSpeed,
        Pierce = template.Pierce,
        Affects = template.Affects,
    };
}
