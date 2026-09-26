using Avalon.Common.Mathematics;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts.Abilities;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Abilities.AbilityTestData;

namespace Avalon.Server.World.UnitTests.Scripts;

public class CircleAbilityScriptShould
{
    private static readonly Vector3 AlongZ = new(0f, 0f, 1f);

    private static void Fire(TestArena arena, CharacterEntity caster, GameAbility ability, Vector3? point = null)
    {
        var script = new CircleAbilityScript(ability, caster, new AbilityAim(AlongZ, point), arena);
        script.Prepare();
        Assert.Equal(SpellState.Finished, script.State);   // a circle resolves once, when it fires
    }

    [Fact]
    public void Damage_every_hostile_in_a_circle_on_the_caster_nearest_first_and_once()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature far = arena.Creature(0f, 3.4f);
        ICreature near = arena.Creature(1f, 0f);
        arena.Creature(0f, 3.6f);                        // just outside 3 + 0.5
        arena.Creature(0f, 1f, invulnerable: true);      // a town NPC is never hit
        arena.Creature(-1f, 0f, health: 0);              // a corpse is never hit
        CharacterEntity bystander = arena.Player(2, 0.5f, 0.5f);   // not flagged: not hostile

        Fire(arena, caster, Game(Circle(201, radius: 3f)));

        Assert.Equal([near, far], arena.Damaged());
        Assert.DoesNotContain(bystander, arena.Damaged());
        Assert.DoesNotContain(caster, arena.Damaged());
    }

    /// <summary>A huge but finite aim point lands exactly Reach from the caster.</summary>
    [Fact]
    public void Clamp_the_aim_point_to_reach()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature atReach = arena.Creature(0f, 15f);

        Fire(arena, caster, Game(AimedCircle(211, reach: 15f, radius: 1f)), point: new Vector3(0f, 0f, 1e30f));

        Assert.Equal([atReach], arena.Damaged());
        Assert.Equal(15f, arena.Fired.Single().Centre!.Value.z, 3);
    }

    [Fact]
    public void Pull_the_centre_back_from_a_wall()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        arena.Navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(new Vector3(0f, 0f, 4f));
        ICreature onOurSide = arena.Creature(0f, 5f);
        arena.Creature(0f, 10f);                     // behind the wall, at the aim point

        Fire(arena, caster, Game(AimedCircle(211, reach: 15f, radius: 1.5f)), point: new Vector3(0f, 0f, 10f));

        Assert.Equal([onOurSide], arena.Damaged());
    }

    /// <summary>A caster off the navmesh (the ray stops where it starts) gets a circle on itself.</summary>
    [Fact]
    public void Centre_on_the_caster_when_the_caster_is_off_the_navmesh()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 2f, 2f);
        arena.Navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => ci.ArgAt<Vector3>(0));

        Fire(arena, caster, Game(AimedCircle(211)), point: new Vector3(2f, 0f, 12f));

        Vector3 centre = arena.Fired.Single().Centre!.Value;
        Assert.Equal(2f, centre.x, 3);
        Assert.Equal(2f, centre.z, 3);
    }

    [Fact]
    public void Heal_the_caster_and_allies_but_never_a_hostile_or_a_creature()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f, pvp: true);
        CharacterEntity friend = arena.Player(2, 1f, 0f);
        CharacterEntity enemy = arena.Player(3, 0f, 1f, pvp: true);
        arena.Creature(0.5f, 0.5f);

        Fire(arena, caster, Game(HealCircle(232)), point: new Vector3(0f, 0f, 0f));

        Assert.Equal([caster, friend], arena.Healed());
        Assert.DoesNotContain(enemy, arena.Healed());
        Assert.Empty(arena.Damaged());
    }

    [Fact]
    public void Tell_the_instance_where_it_landed()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 1f, 2f);

        Fire(arena, caster, Game(Circle(201)));

        (Vector3 origin, Vector3? direction, Vector3? centre) = Assert.Single(arena.Fired);
        Assert.Equal(caster.Position, origin);
        Assert.Null(direction);
        Assert.Equal(caster.Position, centre);
    }

    [Fact]
    public void Spend_the_cast_on_nobody_without_failing()
    {
        var arena = new TestArena();
        Fire(arena, arena.Player(1, 0f, 0f), Game(Circle(201)));
        Assert.Empty(arena.Damaged());
        Assert.Single(arena.Fired);
    }
}
