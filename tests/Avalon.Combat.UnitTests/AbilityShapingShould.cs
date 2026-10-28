using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;

namespace Avalon.Combat.UnitTests;

/// <summary>
/// A capstone's modifiers (#1181 Part D), summed per kind over the capstones held, shape an ability's metadata: a cone's
/// reach and arc (the arc at most 360 degrees), a circle's radius, a projectile's count (at most 9) and its explosion
/// (widened by the area modifier), and the second hit, stack and duration shares as summed. A modifier that does not fit
/// the shape changes nothing.
/// </summary>
public class AbilityShapingShould
{
    private static readonly AbilityMetadata s_cleave = new()
    {
        Name = "Cleave",
        Shape = AbilityShape.Cone,
        Reach = 2.5f,
        ArcDegrees = 100f,
    };

    private static readonly AbilityMetadata s_wideCone = new()
    {
        Name = "Wide",
        Shape = AbilityShape.Cone,
        Reach = 2.5f,
        ArcDegrees = 300f,
    };

    private static readonly AbilityMetadata s_groundSlam = new()
    {
        Name = "Ground Slam",
        Shape = AbilityShape.Circle,
        Radius = 3f,
    };

    private static readonly AbilityMetadata s_frostbolt = new()
    {
        Name = "Frostbolt",
        Shape = AbilityShape.Projectile,
        Reach = 20f,
        ProjectileSpeed = 25f,
    };

    [Theory]
    [InlineData("none", 2.5f, 0f, 100f, 1, 0f, 0f, 0f, 0u, 0f)]
    [InlineData("wide arc", 2.5f, 0f, 150f, 1, 0f, 0f, 0f, 0u, 0f)]
    [InlineData("long reach", 3.5f, 0f, 100f, 1, 0f, 0f, 0f, 0u, 0f)]
    [InlineData("arc capped", 2.5f, 0f, 360f, 1, 0f, 0f, 0f, 0u, 0f)]
    [InlineData("two areas summed", 0f, 4.2f, 0f, 1, 0f, 0f, 0f, 0u, 0f)]
    [InlineData("area on a cone", 2.5f, 0f, 100f, 1, 0f, 0f, 0f, 0u, 0f)]
    [InlineData("fan", 20f, 0f, 0f, 3, 0f, 0f, 0f, 0u, 0f)]
    [InlineData("fan capped", 20f, 0f, 0f, 9, 0f, 0f, 0f, 0u, 0f)]
    [InlineData("explosion widened", 20f, 0f, 0f, 1, 4.5f, 60f, 0f, 0u, 0f)]
    [InlineData("second hit, stacks, duration", 0f, 3f, 0f, 1, 0f, 0f, 50f, 2u, 50f)]
    public void Shape_an_ability_by_its_summed_modifiers(string row, float reach, float radius, float arc,
        int projectiles, float explosionRadius, float explosionEffectiveness, float secondHit, uint stacksBonus,
        float durationPct)
    {
        (AbilityMetadata Template, (TalentModifierKind Kind, int Value, int? Value2)[] Rows) setup = row switch
        {
            "none" => (s_cleave, []),
            "wide arc" => (s_cleave, [(TalentModifierKind.ConeArcPct, 50, null)]),
            "long reach" => (s_cleave, [(TalentModifierKind.ConeReachPct, 40, null)]),
            "arc capped" => (s_wideCone, [(TalentModifierKind.ConeArcPct, 50, null)]),
            "two areas summed" => (s_groundSlam,
                [(TalentModifierKind.AreaPct, 20, null), (TalentModifierKind.AreaPct, 20, null)]),
            "area on a cone" => (s_cleave, [(TalentModifierKind.AreaPct, 40, null)]),
            "fan" => (s_frostbolt, [(TalentModifierKind.AddedProjectiles, 2, null)]),
            "fan capped" => (s_frostbolt,
                [(TalentModifierKind.AddedProjectiles, 8, null), (TalentModifierKind.AddedProjectiles, 8, null)]),
            "explosion widened" => (s_frostbolt,
                [(TalentModifierKind.ImpactExplosion, 30, 60), (TalentModifierKind.AreaPct, 50, null)]),
            "second hit, stacks, duration" => (s_groundSlam,
            [
                (TalentModifierKind.SecondHit, 50, null), (TalentModifierKind.AuraStacks, 2, null),
                (TalentModifierKind.AuraDurationPct, 50, null),
            ]),
            _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
        };
        (AbilityMetadata template, (TalentModifierKind Kind, int Value, int? Value2)[] rows) = setup;

        AbilityShaping shaping = default;
        foreach ((TalentModifierKind kind, int value, int? value2) in rows)
            shaping.Add(kind, value, value2);
        AbilityMetadata shaped = shaping.Apply(template);

        Assert.Equal(rows.Length == 0, shaping.IsNone);
        Assert.Equal(template.Name, shaped.Name);
        Assert.Equal(reach, shaped.Reach, 1e-3f);
        Assert.Equal(radius, shaped.Radius, 1e-3f);
        Assert.Equal(arc, shaped.ArcDegrees, 1e-3f);
        Assert.Equal(projectiles, shaped.ProjectileCount);
        Assert.Equal(explosionRadius, shaped.ExplosionRadius, 1e-3f);
        Assert.Equal(explosionEffectiveness, shaped.ExplosionEffectiveness, 1e-3f);
        Assert.Equal(secondHit, shaped.SecondHitEffectiveness, 1e-3f);
        Assert.Equal(stacksBonus, shaped.AuraStacksBonus);
        Assert.Equal(durationPct, shaped.AuraDurationPct, 1e-3f);
    }
}
