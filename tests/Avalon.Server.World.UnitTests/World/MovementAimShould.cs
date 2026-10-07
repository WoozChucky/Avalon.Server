using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// #716: a Movement skill points from the caster toward the cast's <c>GroundPos</c> (the cursor) when the cast
/// sends one, and along the caster's facing otherwise. The direction is resolved once, in the handler, into the
/// aim's facing, so the start broadcast, the fired broadcast and what is hit agree. The real handler over a real
/// cast system and the real shape scripts, in a <see cref="TestArena" />.
/// </summary>
public class MovementAimShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private readonly TestArena _arena = new();
    private readonly InstanceAbilityCastSystem _casts;
    private readonly CastAbilityHandler _handler;
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly CharacterEntity _caster;
    private readonly ICreature _ahead;
    private readonly ICreature _behind;

    public MovementAimShould()
    {
        IScriptManager scripts = Substitute.For<IScriptManager>();
        scripts.GetAbilityScript(nameof(ConeAbilityScript)).Returns(typeof(ConeAbilityScript));
        _casts = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, Substitute.For<IServiceProvider>(), scripts, _arena);

        IMapInstance instance = Substitute.For<IMapInstance>();
        instance.RunInstantAbility(default!, default, default!).ReturnsForAnyArgs(ci =>
            _casts.RunInstant(ci.ArgAt<IUnit>(0), ci.ArgAt<AbilityAim>(1), ci.ArgAt<IAbility>(2)));
        instance.QueueAbility(default!, default, default!).ReturnsForAnyArgs(ci =>
            _casts.QueueAbility(ci.ArgAt<IUnit>(0), ci.ArgAt<AbilityAim>(1), ci.ArgAt<IAbility>(2)));
        IInstanceRegistry registry = Substitute.For<IInstanceRegistry>();
        registry.GetInstanceById(Arg.Any<Guid>()).Returns(instance);
        IWorld world = Substitute.For<IWorld>();
        world.InstanceRegistry.Returns(registry);
        _handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig(), TimeProvider.System);

        // The caster at the origin, facing +Z (yaw 0); one creature ahead of it on +Z, one behind on -Z.
        _caster = _arena.Player(1, 0f, 0f);
        _caster.Orientation = Vector3.zero;
        _ahead = _arena.Creature(0f, 2f);
        _behind = _arena.Creature(0f, -2f);

        _connection.Character.Returns(_caster);
        _connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
    }

    /// <summary>Cleave's shape: a 2.5 m, 100-degree Movement cone.</summary>
    private GameAbility Learn(uint castTimeMs = 0)
    {
        AbilityTemplate row = AbilityTestData.Cone(200, reach: 2.5f, arc: 100f);
        row.CastTime = castTimeMs;
        GameAbility ability = AbilityTestData.Game(row);
        _caster.Spells.Load([ability]);
        return ability;
    }

    private void Cast(Vector3Dto? groundPos) =>
        _handler.Execute(_connection, new CCastAbilityPacket { AbilityId = 200, GroundPos = groundPos });

    /// <summary>How many packets the caster's connection was sent: a refusal would be one.</summary>
    private int Sent() => _connection.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IWorldConnection.Send));

    [Fact]
    public void Hit_the_creature_behind_when_the_cursor_is_behind_the_caster()
    {
        Learn();

        Cast(new Vector3Dto { X = 0f, Y = 0f, Z = -6f });

        Assert.Equal([_behind], _arena.Damaged());
        (AbilityFootprint fired, _) = Assert.Single(_arena.FiredFootprints);
        Assert.Equal(new Vector3(0f, 0f, -1f), fired.Direction);
        Assert.Equal(Vector3.zero, fired.Origin);   // anchored on the caster
        Assert.Equal(0, Sent());
    }

    /// <summary>Only the direction follows the cursor: a point far past the reach does not stretch the cone.</summary>
    [Fact]
    public void Keep_the_reach_measured_from_the_caster_for_a_far_cursor()
    {
        Learn();
        ICreature farBehind = _arena.Creature(0f, -10f);

        Cast(new Vector3Dto { X = 0f, Y = 0f, Z = -50f });

        Assert.Equal([_behind], _arena.Damaged());
        Assert.DoesNotContain(farBehind, _arena.Damaged());
        Assert.Equal(2.5f, _arena.FiredFootprints.Single().Footprint.Reach);
    }

    public static TheoryData<Vector3Dto?> NoUsablePoint() => new()
    {
        null,
        new Vector3Dto { X = float.NaN, Y = 0f, Z = -6f },
        new Vector3Dto { X = 0f, Y = float.PositiveInfinity, Z = -6f },
        // Within a millimetre of the caster on X/Z: no direction to read, so the facing stands.
        new Vector3Dto { X = 0.0003f, Y = 5f, Z = -0.0004f },
    };

    /// <summary>
    /// A cast without a usable point aims along the facing exactly as before #716, and a Movement skill is never
    /// refused for its point (<c>NoAimPoint</c> stays Cursor-only).
    /// </summary>
    [Theory]
    [MemberData(nameof(NoUsablePoint))]
    public void Hit_the_creature_ahead_along_the_facing_without_a_usable_point(Vector3Dto? groundPos)
    {
        Learn();

        Cast(groundPos);

        Assert.Equal([_ahead], _arena.Damaged());
        Assert.Equal(new Vector3(0f, 0f, 1f), _arena.FiredFootprints.Single().Footprint.Direction);
        Assert.Equal(0, Sent());
    }

    /// <summary>
    /// A cast-time Movement skill keeps the direction captured when it started: its start telegraph and its fired
    /// broadcast carry one direction, and turning mid-cast changes neither.
    /// </summary>
    [Fact]
    public void Fire_a_cast_time_movement_skill_on_the_direction_its_telegraph_showed()
    {
        Learn(castTimeMs: 200);

        Cast(new Vector3Dto { X = 0f, Y = 0f, Z = -6f });
        _caster.Orientation = new Vector3(0f, 90f, 0f);   // turned to +X during the cast
        for (int i = 0; i < 16; i++)
        {
            _casts.Update(s_tick, []);
        }

        (_, _, uint castId, AbilityFootprint? started) = Assert.Single(_arena.Started);
        Assert.Equal(new Vector3(0f, 0f, -1f), started!.Value.Direction);
        (AbilityFootprint fired, uint firedId) = Assert.Single(_arena.FiredFootprints);
        Assert.Equal(started.Value, fired);
        Assert.Equal(castId, firedId);
        Assert.Equal([_behind], _arena.Damaged());
    }

    /// <summary>The instant path's fired footprint is the one the shape resolves from the captured aim.</summary>
    [Fact]
    public void Fire_an_instant_movement_skill_on_the_footprint_the_aim_resolves_to()
    {
        GameAbility ability = Learn();

        Cast(new Vector3Dto { X = 3f, Y = 0f, Z = 4f });

        AbilityFootprint fired = _arena.FiredFootprints.Single().Footprint;
        var aim = new AbilityAim(new Vector3(0.6f, 0f, 0.8f), null) { Origin = Vector3.zero };
        Assert.Equal(AbilityFootprint.Resolve(ability.Metadata, aim, Vector3.zero, _arena.Navigator), fired);
    }
}
