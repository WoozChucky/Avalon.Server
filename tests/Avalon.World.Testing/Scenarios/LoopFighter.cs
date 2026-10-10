using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.World;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// One player fighting through a forest, as a client playing it would: every tick it steers at the nearest live
/// creature with one <see cref="CPlayerInputPacket" /> through the real <see cref="PlayerInputHandler" />, turning away
/// from a wall it walked into (bump and turn), and once the creature is within its basic ability's reach it stands,
/// faces it and casts the ability through the real <see cref="CastAbilityHandler" /> each time the ability is ready.
/// It runs from <see cref="ScenarioConnection.UpdateMap" />, inside the instance pass, where production processes a
/// connection's queued packets. One input and one cast packet per fighter, mutated each tick, so the driver itself
/// allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// It reads the forest directly rather than through the world-state packets a client decodes: the creatures are the
/// forest's own (<see cref="FightForest" />), and the player's position, velocity, cooldown and death are its
/// character's. Its decisions are those of the load-test fighter (<c>tools/Avalon.LoadTest</c>): the class's basic
/// ability (Cleave 200, Arcane Bolt 210, Quick Shot 220, Smite 230: no cost, a 0.8 s cooldown), a stop a metre inside
/// the ability's reach, or a fifth of a short reach (a warrior stops at 2 m, outside the 1.5 m a creature keeps from
/// what it fights, or it would chase it for ever), a Cursor ability aimed at the creature's position, a Movement
/// ability along the facing, turned to the creature by the input's yaw. A cast is handled in the same map update as
/// the tick's input, so inside the input's dispatch (<see cref="ScenarioConnection.UpdateMap" />), not one of its own.
/// </para>
/// <para>
/// <b>Death.</b> A dead player sends nothing, as the input handler drops a dead character's input. A client would ask to
/// respawn in town (<c>CMSG_RESPAWN_AT_TOWN</c>) and walk back in, which a scenario cannot do, since it cannot move a
/// character between instances. Instead, <see cref="ReviveAfterTicks" /> after it died the fighter is revived at the
/// forest's entry through the instance's own <see cref="ICombatService.RevivePlayer" />: the dead flag cleared, a
/// quarter of its health (<c>CombatConfig.ReviveHealthFraction</c>), and the revive broadcast to the instance. The
/// delay stands for the trip to town and back; the forest keeps three fighters for the whole run, and the run stays
/// deterministic.
/// </para>
/// <para>
/// Bump and turn: a step that asked to move but moved less than 0.1 m/s ran into a wall. The fighter then turns 90 to
/// 270 degrees away (from a seeded <see cref="Random" />, so the run is deterministic) and holds that heading for a
/// second before steering at the creature again.
/// </para>
/// </remarks>
public sealed class LoopFighter
{
    /// <summary>How long a dead fighter lies dead before it is revived at the entry: 10 s at 60 Hz.</summary>
    public const int ReviveAfterTicks = 600;

    /// <summary>How long a heading turned away from a wall is held: 1 s at 60 Hz.</summary>
    private const int DetourTicks = 60;

    /// <summary>A step that asked to move and moved less than 0.1 m/s ran into a wall.</summary>
    private const float BlockedSpeedSquared = 0.01f;

    /// <summary>How far inside its ability's reach a fighter stops, at most: 1 m ...</summary>
    private const float ReachMargin = 1f;

    /// <summary>... or this share of a short reach.</summary>
    private const float ReachMarginShare = 0.2f;

    private readonly PlayerInputHandler _input;
    private readonly CastAbilityHandler _cast;
    private readonly TimeProvider _clock;
    private readonly double _gcdMs;
    private readonly Random _rng;
    private readonly IAbility _ability;
    private readonly bool _cursorAim;
    private readonly float _stopDistance;
    private readonly CPlayerInputPacket _inputPacket = new();
    private readonly Vector3Dto _aimPoint = new();
    private readonly CCastAbilityPacket _castPacket;

    private FightForest? _forest;
    private bool _moved;
    private bool _detouring;
    private float _detourYaw;
    private int _detourLeft;
    private int _deadFor;

    /// <param name="handlers">The handlers every fighter of the world shares (<see cref="Handlers" />).</param>
    /// <param name="character">The fighter's character, holding its class's basic ability.</param>
    /// <param name="seed">Seeds its turns away from walls.</param>
    public LoopFighter(FighterHandlers handlers, ICharacter character, int seed)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(character);
        (_input, _cast, _clock, _gcdMs) = (handlers.Input, handlers.Cast, handlers.Clock, handlers.GcdMs);
        _rng = new Random(seed);

        uint abilityId = BasicAbility(character.Class);
        _ability = character.Spells[new AbilityId(abilityId)]
                   ?? throw new InvalidOperationException($"{character.Name} has no basic ability {abilityId}");
        AbilityMetadata meta = _ability.Metadata;
        _cursorAim = meta.AimMode == AbilityAimMode.Cursor;
        _stopDistance = meta.Reach - MathF.Min(ReachMargin, ReachMarginShare * meta.Reach);
        _castPacket = new CCastAbilityPacket { AbilityId = abilityId, GroundPos = _cursorAim ? _aimPoint : null };
    }

    /// <summary>The casts the cast handler accepted.</summary>
    public int Casts { get; private set; }

    /// <summary>The handlers every fighter of a world shares, over the world's real registry, as production has one of each.</summary>
    public static FighterHandlers Handlers(ScenarioWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var combat = new CombatConfig();
        return new FighterHandlers(
            new PlayerInputHandler(NullLogger<PlayerInputHandler>.Instance, world.Host),
            new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world.Host, combat, world.Clock),
            world.Clock, combat.GcdMs);
    }

    /// <summary>The class's basic ability: no cost, a 0.8 s cooldown.</summary>
    public static uint BasicAbility(CharacterClass characterClass) => characterClass switch
    {
        CharacterClass.Warrior => 200u, // Cleave: a cone, along the facing
        CharacterClass.Wizard => 210u, // Arcane Bolt: a projectile, at the cursor
        CharacterClass.Hunter => 220u, // Quick Shot: a projectile, at the cursor
        CharacterClass.Healer => 230u, // Smite: a projectile, at the cursor
        _ => throw new ArgumentOutOfRangeException(nameof(characterClass), characterClass, "No basic ability"),
    };

    /// <summary>The step, to be the fighter's connection's map update; it fights once <see cref="Enter" /> named its forest.</summary>
    public void Step(ScenarioConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        FightForest forest = _forest ?? throw new InvalidOperationException("The fighter was given no forest");
        ICharacter character = connection.Character!;

        if (character.IsDead)
        {
            if (++_deadFor >= ReviveAfterTicks)
            {
                forest.Instance.CombatService.RevivePlayer(character, forest.Entry);
                _deadFor = 0;
                _moved = false;
                _detouring = false;
            }

            return;
        }

        ICreature? target = forest.NearestLive(character.Position);
        if (target is null)
            return;

        Vector3 position = character.Position;
        Vector3 at = target.Position;
        float dx = at.x - position.x;
        float dz = at.z - position.z;
        float goalYaw = Yaw(dx, dz);

        if (dx * dx + dz * dz > _stopDistance * _stopDistance)
        {
            Walk(connection, goalYaw);
            return;
        }

        // In reach: standing, facing it, casting when the ability is ready.
        _detouring = false;
        Send(connection, 0f, 0f, goalYaw);
        _moved = false;

        if (Ready(character))
        {
            _aimPoint.X = at.x;
            _aimPoint.Y = at.y;
            _aimPoint.Z = at.z;
            DateTime before = character.LastCastStartTime;
            _cast.Execute(connection, _castPacket);
            if (character.LastCastStartTime != before)
                Casts++;
        }
    }

    /// <summary>Gives the fighter its forest, once the forest is built: until then its step throws.</summary>
    public void Enter(FightForest forest) => _forest = forest ?? throw new ArgumentNullException(nameof(forest));

    /// <summary>Steers at <paramref name="goalYaw" />, or along a heading turned away from the wall the last step met.</summary>
    private void Walk(ScenarioConnection connection, float goalYaw)
    {
        ICharacter character = connection.Character!;
        Vector3 velocity = character.Velocity;
        bool blocked = _moved && velocity.x * velocity.x + velocity.z * velocity.z < BlockedSpeedSquared;
        if (blocked)
        {
            _detourYaw = ((_detouring ? _detourYaw : goalYaw) + 90f + (float)_rng.NextDouble() * 180f) % 360f;
            _detourLeft = DetourTicks;
            _detouring = true;
        }
        else if (_detouring && --_detourLeft <= 0)
        {
            _detouring = false;
        }

        float yaw = _detouring ? _detourYaw : goalYaw;
        float radians = yaw * (MathF.PI / 180f);
        Send(connection, MathF.Sin(radians), MathF.Cos(radians), yaw);
        _moved = true;
    }

    private void Send(ScenarioConnection connection, float dirX, float dirZ, float yaw)
    {
        _inputPacket.Seq++;
        _inputPacket.DirX = dirX;
        _inputPacket.DirZ = dirZ;
        _inputPacket.YawDeg = (ushort)((int)yaw % 360);
        _input.Execute(connection, _inputPacket);
    }

    /// <summary>
    /// Whether the cast handler would accept the cast: the ability off cooldown and the global cooldown over, as a client
    /// tracks them, so a fighter sends a cast only when it is ready and no refusal is sent. Read from the ability itself,
    /// held since the start: the character's ability lookups (<c>Spells[id]</c>, <c>Spells.IsCasting</c>) allocate, and
    /// a client checks nothing on the server before it casts. The basic abilities are instant, so never mid-cast.
    /// </summary>
    private bool Ready(ICharacter character) =>
        _ability.CooldownTimer <= 0
        && (_clock.GetUtcNow().UtcDateTime - character.LastCastStartTime).TotalMilliseconds >= _gcdMs;

    /// <summary>The yaw, 0 to 360 degrees, that faces along (<paramref name="dx" />, <paramref name="dz" />).</summary>
    private static float Yaw(float dx, float dz)
    {
        float yaw = MathF.Atan2(dx, dz) * (180f / MathF.PI);
        return yaw < 0f ? yaw + 360f : yaw;
    }
}

/// <summary>The packet handlers and the clock every <see cref="LoopFighter" /> of a world shares.</summary>
public sealed record FighterHandlers(PlayerInputHandler Input, CastAbilityHandler Cast, TimeProvider Clock, double GcdMs);

/// <summary>
/// A forest as its fighters see it: the instance, its entry, and its creatures as the forest was built with them, which
/// is every creature it will hold, since creatures never respawn. A creature stays in the list once dead, with no
/// health, after the instance removed its corpse; the list is walked as an array, so a search allocates nothing.
/// </summary>
public sealed class FightForest
{
    private readonly ICreature[] _creatures;
    private readonly int _killedAtStart;

    public FightForest(MapInstance instance)
    {
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        Entry = instance.EntrySpawnWorldPos ?? throw new ArgumentException("The forest has no entry", nameof(instance));
        _creatures = [.. instance.Creatures.Values];
        if (_creatures.Length == 0)
            throw new ArgumentException("The forest has no creatures", nameof(instance));

        _killedAtStart = Dead();
    }

    public MapInstance Instance { get; }

    /// <summary>Where players enter the forest, and where a dead fighter is revived.</summary>
    public Vector3 Entry { get; }

    /// <summary>The creatures the forest was built with.</summary>
    public int Creatures => _creatures.Length;

    /// <summary>The creatures killed since the forest was built.</summary>
    public int Kills => Dead() - _killedAtStart;

    /// <summary>The live creature nearest <paramref name="position" /> on the ground plane; null when none is left.</summary>
    public ICreature? NearestLive(Vector3 position)
    {
        ICreature? nearest = null;
        float best = float.MaxValue;
        foreach (ICreature creature in _creatures)
        {
            if (creature.CurrentHealth == 0)
                continue;

            float dx = creature.Position.x - position.x;
            float dz = creature.Position.z - position.z;
            float distance = dx * dx + dz * dz;
            if (distance < best)
            {
                best = distance;
                nearest = creature;
            }
        }

        return nearest;
    }

    private int Dead()
    {
        int dead = 0;
        foreach (ICreature creature in _creatures)
        {
            if (creature.CurrentHealth == 0)
                dead++;
        }

        return dead;
    }
}
