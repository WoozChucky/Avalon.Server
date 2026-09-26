using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// A bad row is refused with an error naming it, and every other row still loads (#164), the same
/// pattern as the loot and vendor catalogs.
/// </summary>
public class AbilityCatalogShould
{
    public static TheoryData<string, AbilityTemplate> Refused() => new()
    {
        { "negative reach", With(AbilityTestData.Cone(1), t => t.Reach = -1f) },
        { "NaN radius", With(AbilityTestData.Circle(1), t => t.Radius = float.NaN) },
        { "infinite speed", With(AbilityTestData.Projectile(1), t => t.ProjectileSpeed = float.PositiveInfinity) },
        { "circle radius 0", With(AbilityTestData.Circle(1), t => t.Radius = 0f) },
        { "aim point circle on movement", With(AbilityTestData.AimedCircle(1), t => t.AimMode = AbilityAimMode.Movement) },
        { "aim point circle reach 0", With(AbilityTestData.AimedCircle(1), t => t.Reach = 0f) },
        { "caster circle with reach", With(AbilityTestData.Circle(1), t => t.Reach = 2f) },
        { "cone reach 0", With(AbilityTestData.Cone(1), t => t.Reach = 0f) },
        { "cone arc 0", With(AbilityTestData.Cone(1), t => t.ArcDegrees = 0f) },
        { "cone arc above 360", With(AbilityTestData.Cone(1), t => t.ArcDegrees = 361f) },
        { "projectile reach 0", With(AbilityTestData.Projectile(1), t => t.Reach = 0f) },
        { "projectile speed 0", With(AbilityTestData.Projectile(1), t => t.ProjectileSpeed = 0f) },
        { "projectile on movement", With(AbilityTestData.Projectile(1), t => t.AimMode = AbilityAimMode.Movement) },
        { "ally cone", With(AbilityTestData.Cone(1), t => t.Affects = AbilityAffects.Ally) },
        { "ally projectile", With(AbilityTestData.Projectile(1), t => t.Affects = AbilityAffects.Ally) },
        { "unknown shape", With(AbilityTestData.Circle(1), t => t.Shape = (AbilityShape)9) },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Refuse_a_bad_row_and_name_it(string why, AbilityTemplate bad)
    {
        var catalog = new AbilityCatalog([bad, AbilityTestData.Circle(2)], NullLoggerFactory.Instance);

        AbilityRefusal refusal = Assert.Single(catalog.Refused);
        Assert.Equal(1u, refusal.Id.Value);
        Assert.False(string.IsNullOrWhiteSpace(refusal.Reason), why);
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
