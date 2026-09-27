using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// A bad row is refused with an error naming it, and every other row still loads (#164), the same
/// pattern as the loot and vendor catalogs.
/// </summary>
public class AbilityCatalogShould
{
    /// <summary>
    /// Each row names the reason it expects, so it proves its own rule: with that rule removed the row
    /// is either accepted or refused for a different reason, and the test fails.
    /// </summary>
    public static TheoryData<string, string, AbilityTemplate> Refused() => new()
    {
        { "negative cone reach", "Reach -1 is not a finite value", With(AbilityTestData.Cone(1), t => t.Reach = -1f) },
        { "negative caster circle reach", "Reach -1 is not a finite value", With(AbilityTestData.Circle(1), t => t.Reach = -1f) },
        { "NaN radius", "Radius NaN is not a finite value", With(AbilityTestData.Circle(1), t => t.Radius = float.NaN) },
        { "infinite speed", "is not a finite value", With(AbilityTestData.Projectile(1), t => t.ProjectileSpeed = float.PositiveInfinity) },
        { "negative speed on a cone", "ProjectileSpeed -1 is not a finite value", With(AbilityTestData.Cone(1), t => t.ProjectileSpeed = -1f) },
        { "NaN threat multiplier", "ThreatMultiplier NaN is not a finite value", With(AbilityTestData.Cone(1), t => t.ThreatMultiplier = float.NaN) },
        { "infinite threat multiplier", $"ThreatMultiplier {float.PositiveInfinity} is not a finite value", With(AbilityTestData.Cone(1), t => t.ThreatMultiplier = float.PositiveInfinity) },
        { "negative threat multiplier", "ThreatMultiplier -1 is not a finite value", With(AbilityTestData.Cone(1), t => t.ThreatMultiplier = -1f) },
        { "NaN heal threat", "HealThreatPerHp NaN is not a finite value", With(AbilityTestData.HealCircle(1), t => t.HealThreatPerHp = float.NaN) },
        { "infinite heal threat", $"HealThreatPerHp {float.PositiveInfinity} is not a finite value", With(AbilityTestData.HealCircle(1), t => t.HealThreatPerHp = float.PositiveInfinity) },
        { "negative heal threat", "HealThreatPerHp -1 is not a finite value", With(AbilityTestData.HealCircle(1), t => t.HealThreatPerHp = -1f) },
        { "negative scaling coefficient", "ScalingCoefficient -1 is not a finite value", With(AbilityTestData.Cone(1), t => t.ScalingCoefficient = -1f) },
        { "NaN scaling coefficient", "ScalingCoefficient NaN is not a finite value", With(AbilityTestData.Cone(1), t => t.ScalingCoefficient = float.NaN) },
        { "negative weapon coefficient", "WeaponCoefficient -0.5 is not a finite value", With(AbilityTestData.Cone(1), t => t.WeaponCoefficient = -0.5f) },
        { "infinite weapon coefficient", $"WeaponCoefficient {float.PositiveInfinity} is not a finite value", With(AbilityTestData.Cone(1), t => t.WeaponCoefficient = float.PositiveInfinity) },
        { "unknown scaling stat", "unknown scaling stat 9", With(AbilityTestData.Cone(1), t => t.ScalingStat = (ScalingStat)9) },
        { "negative power gain per hit", "PowerGainPerHit -1 is below 0", With(AbilityTestData.Cone(1), t => t.PowerGainPerHit = -1) },
        { "unknown aim mode", "unknown aim mode 9", With(AbilityTestData.Circle(1), t => t.AimMode = (AbilityAimMode)9) },
        { "unknown anchor", "unknown anchor 9", With(AbilityTestData.Circle(1), t => t.Anchor = (AbilityAnchor)9) },
        { "unknown affects", "unknown affects 9", With(AbilityTestData.Circle(1), t => t.Affects = (AbilityAffects)9) },
        { "circle radius 0", "a circle needs a Radius above 0", With(AbilityTestData.Circle(1), t => t.Radius = 0f) },
        { "aim point circle on movement", "a circle on the aim point must aim with the cursor", With(AbilityTestData.AimedCircle(1), t => t.AimMode = AbilityAimMode.Movement) },
        { "aim point circle reach 0", "a circle on the aim point needs a Reach above 0", With(AbilityTestData.AimedCircle(1), t => t.Reach = 0f) },
        { "caster circle with reach", "a circle on the caster must have Reach 0", With(AbilityTestData.Circle(1), t => t.Reach = 2f) },
        { "cone reach 0", "a cone needs a Reach above 0", With(AbilityTestData.Cone(1), t => t.Reach = 0f) },
        { "cone arc 0", "a cone needs ArcDegrees above 0 and at most 360", With(AbilityTestData.Cone(1), t => t.ArcDegrees = 0f) },
        { "cone arc above 360", "a cone needs ArcDegrees above 0 and at most 360", With(AbilityTestData.Cone(1), t => t.ArcDegrees = 361f) },
        { "projectile reach 0", "a projectile needs a Reach above 0", With(AbilityTestData.Projectile(1), t => t.Reach = 0f) },
        { "projectile speed 0", "a projectile needs a ProjectileSpeed above 0", With(AbilityTestData.Projectile(1), t => t.ProjectileSpeed = 0f) },
        { "projectile on movement", "a projectile must aim with the cursor", With(AbilityTestData.Projectile(1), t => t.AimMode = AbilityAimMode.Movement) },
        { "ally cone", "only a circle may affect allies", With(AbilityTestData.Cone(1), t => t.Affects = AbilityAffects.Ally) },
        { "ally projectile", "only a circle may affect allies", With(AbilityTestData.Projectile(1), t => t.Affects = AbilityAffects.Ally) },
        { "circle on the cone script", "a circle must use CircleAbilityScript", With(AbilityTestData.Circle(1), t => t.SpellScript = "ConeAbilityScript") },
        { "cone on the projectile script", "a cone must use ConeAbilityScript", With(AbilityTestData.Cone(1), t => t.SpellScript = "ProjectileAbilityScript") },
        { "projectile on the circle script", "a projectile must use ProjectileAbilityScript", With(AbilityTestData.Projectile(1), t => t.SpellScript = "CircleAbilityScript") },
        { "unknown shape", "unknown shape 9", With(AbilityTestData.Circle(1), t => t.Shape = (AbilityShape)9) },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Refuse_a_bad_row_and_name_it(string why, string reason, AbilityTemplate bad)
    {
        var catalog = new AbilityCatalog([bad, AbilityTestData.Circle(2)], NullLoggerFactory.Instance);

        AbilityRefusal refusal = Assert.Single(catalog.Refused);
        Assert.Equal(1u, refusal.Id.Value);
        Assert.Equal(bad.Name, refusal.Name);
        Assert.True(refusal.Reason.Contains(reason, StringComparison.Ordinal),
            $"{why}: expected a reason containing '{reason}', got '{refusal.Reason}'");
        Assert.False(catalog.TryGet(new AbilityId(1), out _));
        Assert.True(catalog.TryGet(new AbilityId(2), out _), "the good row still loads");
    }

    [Fact]
    public void Accept_one_valid_row_of_every_shape()
    {
        AbilityTemplate[] rows =
        [
            AbilityTestData.Circle(1), AbilityTestData.AimedCircle(2), AbilityTestData.HealCircle(3),
            AbilityTestData.Cone(4), AbilityTestData.Cone(5, arc: 360f, aim: AbilityAimMode.Cursor),
            AbilityTestData.Projectile(6), AbilityTestData.Projectile(7, pierce: true),
        ];

        var catalog = new AbilityCatalog(rows, NullLoggerFactory.Instance);

        Assert.Empty(catalog.Refused);
        Assert.Equal(7, catalog.Count);
        Assert.Equal("7 abilities, 0 refused", catalog.Describe());
    }

    private static AbilityTemplate With(AbilityTemplate template, Action<AbilityTemplate> change)
    {
        change(template);
        return template;
    }
}
