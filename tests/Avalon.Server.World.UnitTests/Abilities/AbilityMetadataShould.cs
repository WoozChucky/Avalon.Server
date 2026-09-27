using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;
using Xunit;

namespace Avalon.Server.World.UnitTests.Abilities;

public class AbilityMetadataShould
{
    [Fact]
    public void Should_default_threat_multiplier_to_one()
    {
        var m = BuildMetadata();
        Assert.Equal(1.0f, m.ThreatMultiplier);
    }

    [Fact]
    public void Should_default_heal_threat_per_hp_to_zero()
    {
        var m = BuildMetadata();
        Assert.Equal(0.0f, m.HealThreatPerHp);
    }

    [Fact]
    public void Should_default_taunt_duration_to_zero()
    {
        var m = BuildMetadata();
        Assert.Equal(0u, m.TauntDurationMs);
    }

    [Fact]
    public void Should_default_flags_to_None()
    {
        var m = BuildMetadata();
        Assert.Equal(AbilityFlags.None, m.Flags);
    }

    [Fact]
    public void Clone_every_shape_field()
    {
        var original = new AbilityMetadata
        {
            Name = "x", ScriptName = "x", AimMode = AbilityAimMode.Cursor, Shape = AbilityShape.Projectile,
            Anchor = AbilityAnchor.AimPoint, Reach = 1f, Radius = 2f, ArcDegrees = 3f, ProjectileSpeed = 4f,
            Pierce = true, Affects = AbilityAffects.Ally, PowerGainPerHit = 5,
        };

        AbilityMetadata clone = original.Clone();

        Assert.Equivalent(original, clone, strict: true);
    }

    /// <summary>#526: the per-hit power gain is copied from the template, and defaults to 0.</summary>
    [Fact]
    public void Map_the_power_gain_per_hit_from_the_template()
    {
        Avalon.Domain.World.AbilityTemplate template = AbilityTestData.Cone(1);
        template.PowerGainPerHit = 8;

        Assert.Equal(8, Avalon.World.Abilities.AbilityMetadataMapper.From(template).PowerGainPerHit);
        Assert.Equal(0, BuildMetadata().PowerGainPerHit);
    }

    private static AbilityMetadata BuildMetadata() =>
        new()
        {
            Name = "TestAbility",
            ScriptName = "TestScript",
        };
}
