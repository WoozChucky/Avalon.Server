using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// Issue #424, projectile side. A fireball is replicated like any other world object and the client
/// extrapolates it as position + Velocity * seconds, so its Velocity has to be in metres per second —
/// the speed it actually flies at — not the unit direction it used to publish.
/// </summary>
public class FireballAbilityScriptShould
{
    /// <summary>FireballAbilityScript.ProjectileSpeed. Private there; the value is the point of the test.</summary>
    private const float ProjectileSpeed = 5f;

    private const float Tick = 1f / 60f;

    private static (FireballAbilityScript Script, ISimulationContext Context) Build(IUnit? target)
    {
        var ability = Substitute.For<IAbility>();
        ability.Metadata.Returns(new AbilityMetadata { Name = "Fireball", ScriptName = nameof(FireballAbilityScript) });

        var caster = Substitute.For<IUnit>();
        caster.Position.Returns(Vector3.zero);

        var context = Substitute.For<ISimulationContext>();

        var script = new FireballAbilityScript(NullLogger<FireballAbilityScript>.Instance, ability, caster, target,
            context);
        return (script, context);
    }

    private static IUnit TargetAt(Vector3 position)
    {
        var target = Substitute.For<IUnit>();
        target.Position.Returns(position);
        return target;
    }

    [Fact]
    public void Publish_Velocity_In_Metres_Per_Second_When_Launched()
    {
        (FireballAbilityScript fireball, _) = Build(TargetAt(new Vector3(10f, 0f, 0f)));

        fireball.Prepare();

        Assert.Equal(ProjectileSpeed, fireball.Velocity.magnitude, 3);
        Assert.True(fireball.Velocity.x > 0f, $"velocity should point at the target, was {fireball.Velocity}");
    }

    [Fact]
    public void Publish_Velocity_In_Metres_Per_Second_In_Flight()
    {
        (FireballAbilityScript fireball, _) = Build(TargetAt(new Vector3(10f, 0f, 0f)));
        fireball.Prepare();

        fireball.Update(TimeSpan.FromSeconds(Tick));

        Assert.Equal(ProjectileSpeed, fireball.Velocity.magnitude, 3);
    }

    /// <summary>What dead reckoning relies on: one tick of flight covers Velocity * deltaTime.</summary>
    [Fact]
    public void Cover_Velocity_Times_Elapsed_Seconds_In_One_Tick()
    {
        (FireballAbilityScript fireball, _) = Build(TargetAt(new Vector3(10f, 0f, 0f)));
        fireball.Prepare();

        Vector3 before = fireball.Position;
        fireball.Update(TimeSpan.FromSeconds(Tick));
        float travelled = (fireball.Position - before).magnitude;

        Assert.Equal(fireball.Velocity.magnitude * Tick, travelled, 4);
        Assert.Equal(ProjectileSpeed * Tick, travelled, 4);
    }

    [Fact]
    public void Come_To_Rest_When_It_Hits()
    {
        // 5 cm from the launch point: inside the 0.1 m hit radius, so the first update is a hit, but
        // not on it, so the fireball launches with a real velocity that the hit has to clear.
        (FireballAbilityScript fireball, ISimulationContext context) = Build(TargetAt(new Vector3(0.05f, 0f, 0f)));
        fireball.Prepare();
        Assert.NotEqual(Vector3.zero, fireball.Velocity);

        fireball.Update(TimeSpan.FromSeconds(Tick));

        Assert.Equal(SpellState.Finished, fireball.State);
        Assert.Equal(Vector3.zero, fireball.Velocity);
        context.CombatService.ReceivedWithAnyArgs(1).ApplyDamage(default!, default!, default, default!);
    }

    /// <summary>
    /// Prepare used to mark itself finished and then dereference the missing target anyway, so a
    /// fireball with no target threw instead of fizzling.
    /// </summary>
    [Fact]
    public void Fizzle_At_Rest_When_It_Has_No_Target()
    {
        (FireballAbilityScript fireball, _) = Build(target: null);

        fireball.Prepare();
        fireball.Update(TimeSpan.FromSeconds(Tick));

        Assert.Equal(SpellState.Finished, fireball.State);
        Assert.Equal(Vector3.zero, fireball.Velocity);
    }
}
