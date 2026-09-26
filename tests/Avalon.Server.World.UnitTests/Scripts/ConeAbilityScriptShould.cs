using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abilities;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Scripts.Abilities;
using Xunit;
using static Avalon.Server.World.UnitTests.Abilities.AbilityTestData;

namespace Avalon.Server.World.UnitTests.Scripts;

public class ConeAbilityScriptShould
{
    private static void Fire(TestArena arena, CharacterEntity caster, GameAbility ability, AbilityAim aim)
    {
        var script = new ConeAbilityScript(ability, caster, aim, arena);
        script.Prepare();
        Assert.Equal(SpellState.Finished, script.State);   // a cone resolves once, when it fires
    }

    [Fact]
    public void Hit_along_the_casters_facing_for_a_movement_cone()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature ahead = arena.Creature(2f, 0f);
        arena.Creature(-2f, 0f);

        // Facing +X; the aim point (behind) is ignored for a Movement skill.
        Fire(arena, caster, Game(Cone(200, reach: 2.5f, arc: 100f)),
            new AbilityAim(new Vector3(1f, 0f, 0f), new Vector3(-5f, 0f, 0f)));

        Assert.Equal([ahead], arena.Damaged());
    }

    [Fact]
    public void Hit_toward_the_aim_point_for_a_cursor_cone()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature towardPoint = arena.Creature(0f, -4f);
        arena.Creature(0f, 4f);

        Fire(arena, caster, Game(Cone(212, reach: 6f, arc: 60f, aim: AbilityAimMode.Cursor)),
            new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -10f)));

        Assert.Equal([towardPoint], arena.Damaged());
    }

    [Fact]
    public void Fall_back_to_facing_when_the_aim_point_is_on_the_caster()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature ahead = arena.Creature(0f, 3f);

        Fire(arena, caster, Game(Cone(212, reach: 6f, arc: 60f, aim: AbilityAimMode.Cursor)),
            new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 0f)));

        Assert.Equal([ahead], arena.Damaged());
    }

    /// <summary>A non-finite yaw reaches the script as +Z, never as NaN, so the cone hits what is ahead on Z.</summary>
    [Fact]
    public void Aim_a_movement_cone_along_Z_for_a_non_finite_yaw()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature ahead = arena.Creature(0f, 2f);
        arena.Creature(0f, -2f);

        Fire(arena, caster, Game(Cone(200, reach: 2.5f, arc: 100f)),
            new AbilityAim(AbilityAim.FacingFromYaw(float.NaN), null));

        Assert.Equal([ahead], arena.Damaged());
    }

    [Fact]
    public void Hit_each_unit_once_nearest_first_and_broadcast_the_direction()
    {
        var arena = new TestArena();
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature far = arena.Creature(0.2f, 2.2f);
        ICreature near = arena.Creature(-0.2f, 1f);

        Fire(arena, caster, Game(Cone(200, reach: 2.5f, arc: 100f)), new AbilityAim(new Vector3(0f, 0f, 1f), null));

        Assert.Equal([near, far], arena.Damaged());
        (Vector3 origin, Vector3? direction, Vector3? centre) = Assert.Single(arena.Fired);
        Assert.Equal(caster.Position, origin);
        Assert.Equal(new Vector3(0f, 0f, 1f), direction);
        Assert.Null(centre);
    }
}
