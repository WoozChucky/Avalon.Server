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
    private readonly ulong _north = new ObjectGuid(ObjectType.Creature, 11).RawValue;
    private readonly ulong _east = new ObjectGuid(ObjectType.Creature, 12).RawValue;
    private readonly BotMetrics _metrics = new();
    private readonly Random _rng = new(1);
    private readonly AvalonCryptoSession _server;
    private readonly PacketCodec _codec;
    private readonly List<TripEnd> _trips = [];
    private long _now = Stopwatch.GetTimestamp();

    public FighterShould()
    {
        AsymmetricCipherKeyPair clientKeys = AsymmetricCipher.GenerateECDHKeyPair();
        var client = new AvalonCryptoSession(CryptoRole.Client, clientKeys);
        _server = new AvalonCryptoSession(CryptoRole.Server);
        _server.Initialize(AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeys)));
        client.Initialize(_server.GetPublicKey());
        _codec = new PacketCodec(client);
    }

    [Fact]
    public void Walk_into_the_forest_fight_walk_out_and_respawn_after_dying()
    {
        // Bot 1 is a wizard: Arcane Bolt (210), 20 m of reach, so it stops 19 m from its target. The forest has a wall
        // filling the inside of the L the fighter walks in by (north, then east): the straight way back runs into it.
        var wizard = new Sim(this, new Fighter(1, _metrics, TimeSpan.FromSeconds(20), firstTripJitter: TimeSpan.Zero))
        {
            Wall = (x, z) => x > 20f && z < 45f,
        };
        wizard.Fighter.TripEnded += _trips.Add;

        // Town: north to the forest portal at (15, 45), asked for the forest only within 2.5 m of it.
        FighterStep step = wizard.RunUntil(s => s.Action != FighterAction.None);
        Assert.Equal(FighterAction.EnterForest, step.Action);
        Assert.Equal(FighterState.Entering, wizard.Fighter.State);
        Assert.InRange(wizard.Distance(15f, 45f), 0f, 2.5f);
        Assert.Equal(15f, wizard.X, 0.01f);

        // The forest, 300 ms after the request: the fighter starts at its entry spawn and sees a creature 55 m north.
        wizard.Transition(Fighter.ForestMapId, _now + Stopwatch.Frequency * 3 / 10);
        wizard.Apply(SInstanceStateAddPacket.Create([Self(alive: true), Creature(_north, 15f, 70f)], _server.Encryptor));

        // It walks into reach and casts its basic ability at the creature's position, standing.
        step = wizard.RunUntil(s => s.Action != FighterAction.None);
        Assert.Equal(FighterState.InForest, wizard.Fighter.State);
        Assert.Equal(FighterAction.Cast, step.Action);
        Assert.Equal((15f, 70f), (step.AimX, step.AimZ));
        Assert.Equal((0f, 0f), (step.DirX, step.DirZ));
        Assert.InRange(wizard.Distance(15f, 70f), 18f, 19f);
        Assert.Equal(210u, wizard.Fighter.AbilityId);

        // Not again within the cooldown and its margin; then again, as long as the creature lives.
        int steps = 0;
        step = wizard.RunUntil(s => s.Action != FighterAction.None, () => steps++);
        Assert.Equal(FighterAction.Cast, step.Action);
        Assert.InRange(steps, 50, 52);

        // The creature it cast at dies (a kill) as another comes into view to the east: the fighter turns east to it.
        wizard.Apply(SInstanceStateUpdatePacket.Create([new ObjectState { Guid = _north, CurrentHealth = 0, IsDead = true }],
            _server.Encryptor));
        wizard.Apply(SInstanceStateAddPacket.Create([Creature(_east, 60f, 51f)], _server.Encryptor));
        step = wizard.RunUntil(s => s.Action != FighterAction.None);
        Assert.Equal((60f, 51f), (step.AimX, step.AimZ));
        Assert.Equal(41f, wizard.X, 0.5f);

        // That one dies too. With nothing else in sight it walks on north until its 20 s in the forest are up, then
        // back the way it came, never into the wall, by the entry spawn to the portal at (15, 5).
        wizard.Apply(SInstanceStateUpdatePacket.Create([new ObjectState { Guid = _east, CurrentHealth = 0, IsDead = true }],
            _server.Encryptor));
        wizard.RunUntil(s => s.Action != FighterAction.None || wizard.Fighter.State == FighterState.ToExit);
        Assert.Equal(FighterState.ToExit, wizard.Fighter.State);
        Assert.True(wizard.Z > 55f);
        bool byEntry = false;
        step = wizard.RunUntil(s => s.Action != FighterAction.None, () => byEntry |= wizard.Distance(15f, 15f) <= 2.5f);
        Assert.Equal(0, wizard.WallHits);
        Assert.True(byEntry);
        Assert.Equal(FighterAction.LeaveForest, step.Action);
        Assert.Equal(FighterState.Leaving, wizard.Fighter.State);
        Assert.InRange(wizard.Distance(15f, 5f), 0f, 2.5f);

        // Town again: the trip is complete, and the next one sets out as soon as an ack is from town (the first step's
        // answers an input sent in the forest, so it stands).
        wizard.Transition(Fighter.TownMapId, _now);
        step = wizard.Step();
        Assert.Equal((FighterState.Town, 0f, 0f), (wizard.Fighter.State, step.DirX, step.DirZ));
        wizard.Step();
        Assert.Equal(FighterState.ToPortal, wizard.Fighter.State);
        Assert.Equal([TripEnd.Completed], _trips);

        // Dying in the forest on the next trip: it asks to respawn at once, again every 5 s, and is in town once moved.
        wizard.RunUntil(s => s.Action == FighterAction.EnterForest);
        wizard.Transition(Fighter.ForestMapId, _now);
        wizard.Apply(SInstanceStateAddPacket.Create([Self(alive: false)], _server.Encryptor));
        step = wizard.RunUntil(s => s.Action != FighterAction.None);
        Assert.Equal(FighterAction.Respawn, step.Action);
        Assert.Equal(FighterState.Dead, wizard.Fighter.State);
        steps = 0;
        Assert.Equal(FighterAction.Respawn, wizard.RunUntil(s => s.Action != FighterAction.None, () => steps++).Action);
        Assert.InRange(steps, 299, 301);
        wizard.Transition(Fighter.TownMapId, _now);
        wizard.Step();
        wizard.Step();
        Assert.Equal(FighterState.ToPortal, wizard.Fighter.State);
        Assert.Equal([TripEnd.Completed, TripEnd.Died], _trips);

        StepClientValues values = _metrics.TakeWindow();
        Assert.Equal(2, values.ForestEntries);
        Assert.InRange(values.ForestEntryP95, 299.0, 301.0);
        Assert.Equal(3, values.CastsSent);
        Assert.Equal(2, values.Kills);
        Assert.Equal(1, values.ForestTrips);
        Assert.Equal(1, values.OwnDeaths);
        Assert.Empty(values.FighterFailures);

        // Bot 0 is a warrior: Cleave (200) reaches 2.5 m, and a creature holds its station 1.5 m from what it fights, so
        // the warrior stops 2 m from its target, where its swing lands, rather than chasing it to 1.5 m.
        var warrior = new Sim(this, new Fighter(0, _metrics, TimeSpan.FromSeconds(20), firstTripJitter: TimeSpan.Zero));
        warrior.RunUntil(s => s.Action == FighterAction.EnterForest);
        warrior.Transition(Fighter.ForestMapId, _now);
        warrior.Apply(SInstanceStateAddPacket.Create([Self(alive: true), Creature(_north, 15f, 30f)], _server.Encryptor));
        step = warrior.RunUntil(s => s.Action != FighterAction.None);
        Assert.Equal(FighterAction.Cast, step.Action);
        Assert.Equal(200u, warrior.Fighter.AbilityId);
        Assert.InRange(warrior.Distance(15f, 30f), 1.9f, 2f);
    }

    /// <summary>The character as the world describes it to its own player: no position (that travels on the acks).</summary>
    private ObjectState Self(bool alive) =>
        new() { Guid = _self, Health = 120, CurrentHealth = alive ? 120u : 0u, IsDead = !alive };

    private static ObjectState Creature(ulong guid, float x, float z) =>
        new() { Guid = guid, Position = new Vec3 { X = x, Y = 1, Z = z }, Health = 50, CurrentHealth = 50, IsDead = false };

    /// <summary>
    /// One fighter driven as the input driver drives it, its character moved as the world would: at a walk, answered
    /// at once, stopped (and the next step blocked) by the <see cref="Wall"/>.
    /// </summary>
    private sealed class Sim(FighterShould test, Fighter fighter)
    {
        private float _velX;
        private float _velZ;
        private uint _seq;
        private bool _blocked;

        public Fighter Fighter => fighter;

        public float X { get; private set; } = 15f;

        public float Z { get; private set; } = 15f;

        /// <summary>Where the character cannot walk.</summary>
        public Func<float, float, bool>? Wall { get; init; }

        /// <summary>Steps the fighter walked into the wall.</summary>
        public int WallHits { get; private set; }

        /// <summary>Steps until <paramref name="done"/> holds for a step, which is returned.</summary>
        public FighterStep RunUntil(Func<FighterStep, bool> done, Action? each = null)
        {
            for (int i = 0; i < 20_000; i++)
            {
                FighterStep step = Step();
                each?.Invoke();
                if (done(step)) return step;
            }

            throw new InvalidOperationException($"No such step within 20,000; the fighter is {fighter.State} at ({X}, {Z}).");
        }

        public FighterStep Step()
        {
            test._now += s_stepTicks;
            // The ack is the last input's, answered before this one is sent.
            uint seq = ++_seq;
            FighterStep step = fighter.Step(new BotAck(seq - 1, X, Z, _velX, _velZ), _blocked, canSend: true, test._self,
                seq, test._now, test._rng);
            float x = X + step.DirX * Speed / InputDriver.StepsPerSecond;
            float z = Z + step.DirZ * Speed / InputDriver.StepsPerSecond;
            _blocked = Wall?.Invoke(x, z) == true;
            if (_blocked)
            {
                WallHits++;
                (_velX, _velZ) = (0f, 0f);
                return step;
            }

            (_velX, _velZ) = (step.DirX * Speed, step.DirZ * Speed);
            (X, Z) = (x, z);
            return step;
        }

        /// <summary>A map transition as the connection hands it over: the table cleared first, the character at the map's spawn.</summary>
        public void Transition(ushort mapId, long at)
        {
            fighter.Table.Clear();
            fighter.OnTransition(MapTransitionResult.Success, mapId, at);
            (X, Z) = (15f, 15f);
        }

        public void Apply(NetworkPacket packet) => fighter.Table.Apply(packet, test._codec);

        public float Distance(float x, float z) => MathF.Sqrt((X - x) * (X - x) + (Z - z) * (Z - z));
    }
}
