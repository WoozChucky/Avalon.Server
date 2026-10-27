using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>Ability templates the catalog accepts, one per shape. Each test changes only what it is about.</summary>
internal static class AbilityTestData
{
    /// <summary>
    /// A ready-to-cast ability built from <paramref name="template" /> through the production mapper, its
    /// cast timer full as character select sets it.
    /// </summary>
    public static GameAbility Game(AbilityTemplate template) => new()
    {
        AbilityId = template.Id,
        Metadata = AbilityMetadataMapper.From(template),
        CastTimeTimer = (float)template.CastTime / 1000,
        CooldownTimer = 0f,
    };

    /// <summary>
    /// <paramref name="template" /> as a Spell that deals exactly <paramref name="amount" /> for a caster with no offence
    /// (#1178): a spell base of <paramref name="amount" />..<paramref name="amount" />, growing with no level. Its hit draws
    /// that one roll before the hit's chances.
    /// </summary>
    public static AbilityTemplate Dealing(this AbilityTemplate template, uint amount)
    {
        template.Tags = AbilityTags.Spell
                        | (template.Shape == AbilityShape.Projectile ? AbilityTags.Projectile : AbilityTags.Area);
        template.SpellBaseMin = amount;
        template.SpellBaseMax = amount;
        template.SpellBasePerLevelPct = 0f;
        return template;
    }

    public static AbilityTemplate Circle(uint id, string name = "Circle", float radius = 3f) => new()
    {
        Id = new AbilityId(id),
        Name = name,
        ScriptName = "CircleAbilityScript",
        Shape = AbilityShape.Circle,
        AimMode = AbilityAimMode.Movement,
        Anchor = AbilityAnchor.Caster,
        Radius = radius,
        Effects = SpellEffect.Damage,
        DamageType = DamageType.Physical,
        Tags = AbilityTags.Attack | AbilityTags.Area,
    };

    public static AbilityTemplate AimedCircle(uint id, float reach = 15f, float radius = 3f) => new()
    {
        Id = new AbilityId(id),
        Name = "Aimed circle",
        ScriptName = "CircleAbilityScript",
        Shape = AbilityShape.Circle,
        AimMode = AbilityAimMode.Cursor,
        Anchor = AbilityAnchor.AimPoint,
        Reach = reach,
        Radius = radius,
        Effects = SpellEffect.Damage,
        DamageType = DamageType.Physical,
        Tags = AbilityTags.Attack | AbilityTags.Area,
    };

    public static AbilityTemplate HealCircle(uint id, float reach = 15f, float radius = 4f) => new()
    {
        Id = new AbilityId(id),
        Name = "Heal circle",
        ScriptName = "CircleAbilityScript",
        Shape = AbilityShape.Circle,
        AimMode = AbilityAimMode.Cursor,
        Anchor = AbilityAnchor.AimPoint,
        Reach = reach,
        Radius = radius,
        Affects = AbilityAffects.Ally,
        Effects = SpellEffect.Heal,
        HealThreatPerHp = 0.5f,
        Tags = AbilityTags.Heal | AbilityTags.Area,
        SpellBaseMin = 40,
        SpellBaseMax = 40,
    };

    public static AbilityTemplate Cone(uint id, float reach = 3f, float arc = 90f,
        AbilityAimMode aim = AbilityAimMode.Movement) => new()
        {
            Id = new AbilityId(id),
            Name = "Cone",
            ScriptName = "ConeAbilityScript",
            Shape = AbilityShape.Cone,
            AimMode = aim,
            Reach = reach,
            ArcDegrees = arc,
            Effects = SpellEffect.Damage,
            DamageType = DamageType.Physical,
            Tags = AbilityTags.Attack | AbilityTags.Area,
        };

    public static AbilityTemplate Projectile(uint id, float reach = 20f, float speed = 20f, bool pierce = false) => new()
    {
        Id = new AbilityId(id),
        Name = "Projectile",
        ScriptName = "ProjectileAbilityScript",
        Shape = AbilityShape.Projectile,
        AimMode = AbilityAimMode.Cursor,
        Reach = reach,
        ProjectileSpeed = speed,
        Pierce = pierce,
        Effects = SpellEffect.Damage,
        DamageType = DamageType.Physical,
        Tags = AbilityTags.Attack | AbilityTags.Projectile,
    };
}
