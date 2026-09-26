using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts.Abilities;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Abilities.AbilityTestData;

namespace Avalon.Server.World.UnitTests.Scripts;

public class ProjectileAbilityScriptShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    private static ProjectileAbilityScript Launch(TestArena arena, CharacterEntity caster, Domain.World.AbilityTemplate template,
        Vector3 point)
    {
        var script = new ProjectileAbilityScript(Game(template), caster, new AbilityAim(new Vector3(0f, 0f, 1f), point), arena);
        script.Prepare();
        return script;
    }

    private static int Run(ProjectileAbilityScript script, TimeSpan dt, int maxTicks = 10_000)
    {
        int ticks = 0;
        while (script.State is not SpellState.Finished && ticks++ < maxTicks)
            script.Update(dt);
        return ticks;
    }

    [Fact]
    public void Fly_toward_the_aim_point_at_its_speed_in_metres_per_second()
    {
        var arena = new TestArena();
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(210, speed: 22f),
            new Vector3(3f, 50f, 4f));

        Assert.Equal(ObjectType.SpellProjectile, script.Guid.Type);
        Assert.Equal(0f, script.Velocity.y);
        Assert.Equal(22f, script.Velocity.magnitude, 3);
        Assert.Equal(0.6f * 22f, script.Velocity.x, 3);
    }

    [Fact]
    public void Stop_at_the_first_hit_without_pierce()
    {
        var arena = new TestArena();
        ICreature first = arena.Creature(0f, 5f);
        arena.Creature(0f, 8f);
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(220), new Vector3(0f, 0f, 20f));

        Run(script, Tick);

        Assert.Equal([first], arena.Damaged());
        Assert.Equal(Vector3.zero, script.Velocity);
    }

    [Fact]
    public void Hit_each_unit_once_and_fly_on_with_pierce()
    {
        var arena = new TestArena();
        ICreature first = arena.Creature(0f, 5f);
        ICreature second = arena.Creature(0.3f, 8f);
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(221, reach: 30f, speed: 24f, pierce: true),
            new Vector3(0f, 0f, 30f));

        Run(script, Tick);

        Assert.Equal([first, second], arena.Damaged());
    }

    /// <summary>A huge but finite aim point: the projectile flies toward it and stops at Reach.</summary>
    [Fact]
    public void End_at_reach()
    {
        var arena = new TestArena();
        arena.Creature(0f, 21f);   // one metre past reach
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ProjectileAbilityScript script = Launch(arena, caster, Projectile(210, reach: 20f), new Vector3(0f, 0f, 1e30f));

        Run(script, Tick);

        Assert.Empty(arena.Damaged());
        Assert.Equal(20f, script.Position.z, 2);
    }

    [Fact]
    public void End_at_a_wall_and_never_hit_what_is_behind_it()
    {
        var arena = new TestArena();
        arena.Navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>())
            .Returns(ci =>
            {
                Vector3 to = ci.ArgAt<Vector3>(1);
                return to.z > 6f ? new Vector3(to.x, to.y, 6f) : to;   // a wall across z = 6
            });
        arena.Creature(0f, 9f);
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(210), new Vector3(0f, 0f, 20f));

        Run(script, Tick);

        Assert.Empty(arena.Damaged());
        Assert.Equal(6f, script.Position.z, 2);
    }

    /// <summary>A long tick sweeps the whole step, so nothing is tunnelled through.</summary>
    [Fact]
    public void Hit_a_unit_inside_one_long_step()
    {
        var arena = new TestArena();
        ICreature target = arena.Creature(0f, 10f);
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(210, reach: 20f, speed: 20f),
            new Vector3(0f, 0f, 20f));

        script.Update(TimeSpan.FromSeconds(1));

        Assert.Equal([target], arena.Damaged());
        Assert.Equal(SpellState.Finished, script.State);
    }

    /// <summary>A first hit stops the projectile where it met the unit, not at the end of its step.</summary>
    [Fact]
    public void Stop_at_the_hit_point_not_at_the_end_of_its_step()
    {
        var arena = new TestArena();
        arena.Creature(0.3f, 10f);
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(210, reach: 20f, speed: 20f),
            new Vector3(0f, 0f, 20f));

        script.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(SpellState.Finished, script.State);
        Assert.Equal(0f, script.Position.x, 3);
        Assert.Equal(10f, script.Position.z, 3);   // the closest point of the step to the unit's centre
    }

    [Fact]
    public void Stay_where_it_is_on_a_zero_length_tick()
    {
        var arena = new TestArena();
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(210), new Vector3(0f, 0f, 20f));
        Vector3 before = script.Position;

        script.Update(TimeSpan.Zero);

        Assert.Equal(SpellState.Executing, script.State);
        Assert.Equal(before, script.Position);
        Assert.Empty(arena.Damaged());
    }

    [Fact]
    public void Pass_over_a_dead_creature_and_hit_the_live_one_behind_it()
    {
        var arena = new TestArena();
        arena.Creature(0f, 5f, health: 0);
        ICreature alive = arena.Creature(0f, 8f);
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(210), new Vector3(0f, 0f, 20f));

        Run(script, Tick);

        Assert.Equal([alive], arena.Damaged());
    }

    /// <summary>A long tick with nothing in the way still stops travel at Reach.</summary>
    [Fact]
    public void Stop_a_long_step_at_reach()
    {
        var arena = new TestArena();
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(210, reach: 5f, speed: 20f),
            new Vector3(0f, 0f, 20f));

        script.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(SpellState.Finished, script.State);
        Assert.Equal(5f, script.Position.z, 3);
    }

    /// <summary>An off-navmesh caster's ray returns its start, and the projectile must still end.</summary>
    [Fact]
    public void End_on_the_first_tick_when_the_caster_is_off_the_navmesh()
    {
        var arena = new TestArena();
        arena.Navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => ci.ArgAt<Vector3>(0));
        ProjectileAbilityScript script = Launch(arena, arena.Player(1, 0f, 0f), Projectile(210), new Vector3(0f, 0f, 20f));

        Assert.Equal(1, Run(script, Tick));
        Assert.Equal(SpellState.Finished, script.State);
    }

    [Fact]
    public void Pass_over_the_caster_and_players_it_is_not_hostile_to()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f, pvp: true);
        arena.Player(2, 0f, 3f);                         // not flagged
        CharacterEntity enemy = arena.Player(3, 0f, 6f, pvp: true);
        ProjectileAbilityScript script = Launch(arena, caster, Projectile(230), new Vector3(0f, 0f, 20f));

        Run(script, Tick);

        Assert.Equal([enemy], arena.Damaged());
    }

    /// <summary>
    /// A queued cast's script is built when the cast starts, so the projectile leaves from where the
    /// caster stands when it fires, not where it stood when the script was built (#164).
    /// </summary>
    [Fact]
    public void Launch_from_where_the_caster_stands_when_it_fires_not_when_it_was_built()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        var script = new ProjectileAbilityScript(Game(Projectile(210)), caster,
            new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(20f, 0f, 20f)), arena);

        caster.Position = new Vector3(20f, 0f, 0f);
        script.Prepare();

        Assert.Equal(20f, script.Position.x, 3);
        Assert.Equal(0f, script.Position.z, 3);
        Assert.Equal(ProjectileAbilityScript.HeightOffset.y, script.Position.y, 3);
        Assert.Equal(0f, script.Velocity.x, 3);   // straight toward (20, 20) from (20, 0)
    }
}
