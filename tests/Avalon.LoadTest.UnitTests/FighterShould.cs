using System.Diagnostics;
using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.LoadTest.Bots;
using Avalon.LoadTest.Wire;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Org.BouncyCastle.Crypto;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class FighterShould
{
    private const float Speed = 5f;
    private static readonly long s_stepTicks = Stopwatch.Frequency / InputDriver.StepsPerSecond;

    private readonly ulong _self = new ObjectGuid(ObjectType.Character, 7).RawValue;
    private readonly ulong _creature = new ObjectGuid(ObjectType.Creature, 11).RawValue;
    private readonly BotMetrics _metrics = new();
    private readonly Random _rng = new(1);
    private readonly AvalonCryptoSession _server;
    private readonly PacketCodec _codec;
    private readonly Fighter _fighter;
    private readonly List<TripEnd> _trips = [];
    private float _x = 15f;
    private float _z = 15f;
    private float _velX;
    private float _velZ;
    private uint _seq;
    private long _now = Stopwatch.GetTimestamp();

    public FighterShould()
    {
        AsymmetricCipherKeyPair clientKeys = AsymmetricCipher.GenerateECDHKeyPair();
        var client = new AvalonCryptoSession(CryptoRole.Client, clientKeys);
        _server = new AvalonCryptoSession(CryptoRole.Server);
        _server.Initialize(AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeys)));
        client.Initialize(_server.GetPublicKey());
        _codec = new PacketCodec(client);

        // Bot 1 is a wizard: Arcane Bolt (210), 20 m of reach, so it stops 19 m from its target.
        _fighter = new Fighter(1, _metrics, TimeSpan.FromSeconds(20), firstTripJitter: TimeSpan.Zero);
        _fighter.TripEnded += _trips.Add;
    }

    [Fact]
    public void Walk_into_the_forest_fight_walk_out_and_respawn_after_dying()
    {
        // Town: north to the forest portal at (15, 45), asked for the forest only within 2.5 m of it.
        FighterStep step = RunUntil(s => s.Action != FighterAction.None);
        Assert.Equal(FighterAction.EnterForest, step.Action);
        Assert.Equal(FighterState.Entering, _fighter.State);
        Assert.InRange(Distance(15f, 45f), 0f, 2.5f);
        Assert.Equal(15f, _x, 0.01f);

        // The forest, 300 ms after the request: the fighter starts at its entry spawn and sees a creature 25 m north.
        Transition(Fighter.ForestMapId, _now + Stopwatch.Frequency * 3 / 10);
        Apply(SInstanceStateAddPacket.Create([Self(alive: true), Creature(15f, 40f)], _server.Encryptor));

        // It walks into reach and casts its basic ability at the creature's position, standing.
        step = RunUntil(s => s.Action != FighterAction.None);
        Assert.Equal(FighterState.InForest, _fighter.State);
        Assert.Equal(FighterAction.Cast, step.Action);
        Assert.Equal((15f, 40f), (step.AimX, step.AimZ));
        Assert.Equal((0f, 0f), (step.DirX, step.DirZ));
        Assert.InRange(Distance(15f, 40f), 18f, 19f);
        Assert.Equal(210u, _fighter.AbilityId);

        // Not again within the cooldown and its margin; then again, as long as the creature lives.
        int steps = 0;
        step = RunUntil(s => s.Action != FighterAction.None, () => steps++);
        Assert.Equal(FighterAction.Cast, step.Action);
        Assert.InRange(steps, 50, 52);

        // The creature it cast at dies: one kill. With nothing else in sight it walks on north, away from the entry,
        // until its 20 s in the forest are up, then back by the entry spawn to the portal at (15, 5).
        Apply(SInstanceStateUpdatePacket.Create([new ObjectState { Guid = _creature, CurrentHealth = 0, IsDead = true }],
            _server.Encryptor));
        step = RunUntil(s => s.Action != FighterAction.None || _fighter.State == FighterState.ToExit);
        Assert.Equal(FighterState.ToExit, _fighter.State);
        Assert.True(_z > 40f);
        bool byEntry = false;
        step = RunUntil(s => s.Action != FighterAction.None, () => byEntry |= Distance(15f, 15f) <= 2.5f);
        Assert.True(byEntry);
        Assert.Equal(FighterAction.LeaveForest, step.Action);
        Assert.Equal(FighterState.Leaving, _fighter.State);
        Assert.InRange(Distance(15f, 5f), 0f, 2.5f);

        // Town again: the trip is complete, and the next one sets out at once.
        Transition(Fighter.TownMapId, _now);
        Step();
        Assert.Equal(FighterState.ToPortal, _fighter.State);
        Assert.Equal([TripEnd.Completed], _trips);

        // Dying in the forest on the next trip: it asks to respawn at once, again every 5 s, and is in town once moved.
        RunUntil(s => s.Action == FighterAction.EnterForest);
        Transition(Fighter.ForestMapId, _now);
        Apply(SInstanceStateAddPacket.Create([Self(alive: false)], _server.Encryptor));
        step = RunUntil(s => s.Action != FighterAction.None);
        Assert.Equal(FighterAction.Respawn, step.Action);
        Assert.Equal(FighterState.Dead, _fighter.State);
        steps = 0;
        Assert.Equal(FighterAction.Respawn, RunUntil(s => s.Action != FighterAction.None, () => steps++).Action);
        Assert.InRange(steps, 299, 301);
        Transition(Fighter.TownMapId, _now);
        Step();
        Assert.Equal(FighterState.ToPortal, _fighter.State);
        Assert.Equal([TripEnd.Completed, TripEnd.Died], _trips);

        StepClientValues values = _metrics.TakeWindow();
        Assert.Equal(2, values.ForestEntries);
        Assert.InRange(values.ForestEntryP95, 299.0, 301.0);
        Assert.Equal(2, values.CastsSent);
        Assert.Equal(1, values.Kills);
        Assert.Equal(1, values.ForestTrips);
        Assert.Equal(1, values.OwnDeaths);
        Assert.Empty(values.FighterFailures);
    }

    /// <summary>Steps until <paramref name="done"/> holds for a step, which is returned; each step moves the character as the world would.</summary>
    private FighterStep RunUntil(Func<FighterStep, bool> done, Action? each = null)
    {
        for (int i = 0; i < 10_000; i++)
        {
            FighterStep step = Step();
            each?.Invoke();
            if (done(step)) return step;
        }

        throw new InvalidOperationException($"No such step within 10,000; the fighter is {_fighter.State} at ({_x}, {_z}).");
    }

    private FighterStep Step()
    {
        _now += s_stepTicks;
        FighterStep step = _fighter.Step(new BotAck(++_seq, _x, _z, _velX, _velZ), blocked: false, canSend: true, _self,
            _now, _rng);
        (_velX, _velZ) = (step.DirX * Speed, step.DirZ * Speed);
        _x += _velX / InputDriver.StepsPerSecond;
        _z += _velZ / InputDriver.StepsPerSecond;
        return step;
    }

    /// <summary>A map transition as the connection hands it over: the table cleared first, the character at the map's spawn.</summary>
    private void Transition(ushort mapId, long at)
    {
        _fighter.Table.Clear();
        _fighter.OnTransition(MapTransitionResult.Success, mapId, at);
        (_x, _z) = (15f, 15f);
    }

    private void Apply(NetworkPacket packet) => _fighter.Table.Apply(packet, _codec);

    private float Distance(float x, float z) => MathF.Sqrt((_x - x) * (_x - x) + (_z - z) * (_z - z));

    /// <summary>The character as the world describes it to its own player: no position (that travels on the acks).</summary>
    private ObjectState Self(bool alive) =>
        new() { Guid = _self, Health = 120, CurrentHealth = alive ? 120u : 0u, IsDead = !alive };

    private ObjectState Creature(float x, float z) =>
        new() { Guid = _creature, Position = new Vec3 { X = x, Y = 1, Z = z }, Health = 50, CurrentHealth = 50, IsDead = false };
}
