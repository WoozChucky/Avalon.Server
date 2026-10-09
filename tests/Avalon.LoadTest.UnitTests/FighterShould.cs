using System.Diagnostics;
using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.LoadTest.Bots;
using Avalon.LoadTest.Wire;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;
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

    public FighterShould() => (_codec, _server) = Session();

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
        // The repeated ask, answered after the move it repeats: the move stands.
        wizard.Fighter.OnTransition(MapTransitionResult.MoveInProgress, Fighter.TownMapId, _now);
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

    [Fact]
    public async Task Form_a_party_with_the_party_packets_and_fight_solo_once_it_fails_twice_or_falls_apart()
    {
        var world = new PartyWorld();
        FakeMember[] members = [new("LtAAA0", world), new("LtAAA1", world), new("LtAAA2", world)];
        var party = new BotParty(members, _metrics, departJitter: TimeSpan.Zero);
        for (int i = 0; i < members.Length; i++) members[i].Link = party.Links[i];
        var leader = new Sim(this, new Fighter(0, _metrics, TimeSpan.FromSeconds(20), firstTripJitter: TimeSpan.Zero)
        {
            ReadyToLeaveTown = party.Links[0].ReadyToLeave,
        });

        // While the party forms, its members stand in town.
        for (int i = 0; i < 600; i++) leader.Step();
        Assert.Equal((FighterState.Town, 15f, 15f), (leader.Fighter.State, leader.X, leader.Z));

        // The first invite of the last member is refused (it was not online yet): the attempt fails, and the next one,
        // from scratch (each member, the leader last, declines and leaves, which ends the pair formed so far), forms
        // the party. The leader is its first member, the others joined in order.
        world.Refuse("LtAAA2", PartyResult.NotFound, times: 1);
        Assert.True(await PartyFormer.FormAsync(party, CancellationToken.None));
        Assert.Equal(PartyState.Formed, party.State);
        Assert.Equal(["LtAAA0", "LtAAA1", "LtAAA2"], world.Joined);
        Assert.Equal(["LtAAA1", "LtAAA2", "LtAAA1", "LtAAA2"], world.Invited);
        Assert.Equal(["LtAAA2", "LtAAA1", "LtAAA0", "LtAAA2", "LtAAA1", "LtAAA0"], world.Left);

        // Formed: it sets out.
        _now = Math.Max(_now, party.DepartAt);
        leader.Step();
        Assert.Equal(FighterState.ToPortal, leader.Fighter.State);

        // It reconnects (a trip that failed, say): the world keeps an offline member in its party and sends it the roster
        // as its character spawns, and it sets out again at once.
        members[0].Reconnect();
        world.SendRoster(members[0]);
        leader.Fighter.Reset();
        leader.Step();
        Assert.Equal(FighterState.ToPortal, leader.Fighter.State);

        // The world restarts and forgets every party: the next login brings no roster. The fighter waits 20 s for one,
        // then the party has fallen apart: counted once, every member leaves what is left of it, and they fight solo.
        members[0].Reconnect();
        leader.Fighter.Reset();
        int held = 0;
        leader.RunUntil(_ => leader.Fighter.State == FighterState.ToPortal, () => held++);
        Assert.InRange(held, 20 * InputDriver.StepsPerSecond, 20 * InputDriver.StepsPerSecond + 2);
        Assert.Equal(PartyState.Solo, party.State);
        Assert.True(SpinWait.SpinUntil(() => world.Left.Count == 9, TimeSpan.FromSeconds(5)));
        Assert.True(party.Links[1].ReadyToLeave(_now));

        // A party whose invite is refused twice fights solo: counted once, after its members left what was formed.
        FakeMember[] pair = [new("LtAAA3", world), new("LtAAA4", world)];
        var solo = new BotParty(pair, _metrics, departJitter: TimeSpan.Zero);
        for (int i = 0; i < pair.Length; i++) pair[i].Link = solo.Links[i];
        world.Refuse("LtAAA4", PartyResult.NotFound, times: 2);
        Assert.False(await PartyFormer.FormAsync(solo, CancellationToken.None));
        Assert.Equal(PartyState.Solo, solo.State);
        Assert.Equal(["LtAAA4", "LtAAA3", "LtAAA4", "LtAAA3"], world.Left[9..13]);
        Assert.Equal(["LtAAA3", "LtAAA4"], world.Left[13..].Order(StringComparer.Ordinal));
        Assert.True(solo.Links[1].ReadyToLeave(Math.Max(_now, solo.DepartAt)));

        StepClientValues values = _metrics.TakeWindow();
        Assert.Equal(1, values.PartiesFormed);
        Assert.Equal([("party:fell-apart", 1), ("party:invite:NotFound", 1)],
            values.PartyFormFailures.OrderBy(kind => kind.Key, StringComparer.Ordinal).Select(kind => (kind.Key, kind.Value)));
    }

    /// <summary>A client's session and the server's, keyed to each other.</summary>
    private static (PacketCodec Client, AvalonCryptoSession Server) Session()
    {
        AsymmetricCipherKeyPair clientKeys = AsymmetricCipher.GenerateECDHKeyPair();
        var client = new AvalonCryptoSession(CryptoRole.Client, clientKeys);
        var server = new AvalonCryptoSession(CryptoRole.Server);
        server.Initialize(AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeys)));
        client.Initialize(server.GetPublicKey());
        return (new PacketCodec(client), server);
    }

    /// <summary>The character as the world describes it to its own player: no position (that travels on the acks).</summary>
    private ObjectState Self(bool alive) =>
        new() { Guid = _self, Health = 120, CurrentHealth = alive ? 120u : 0u, IsDead = !alive };

    private static ObjectState Creature(ulong guid, float x, float z) =>
        new() { Guid = guid, Position = new Vec3 { X = x, Y = 1, Z = z }, Health = 50, CurrentHealth = 50, IsDead = false };

    /// <summary>
    /// The world's party rules as far as forming one goes, each request answered as the world answers it (the invite
    /// sent before the inviter's answer, the rosters before the joiner's) with the packets the server builds; one party
    /// at most.
    /// </summary>
    private sealed class PartyWorld
    {
        private readonly Lock _lock = new();
        private readonly List<FakeMember> _members = [];
        private readonly List<FakeMember> _party = [];
        private readonly List<string> _invited = [];
        private readonly List<string> _left = [];

        /// <summary>The invites held: the target's inviter, by target.</summary>
        private readonly Dictionary<FakeMember, FakeMember> _invites = [];
        private readonly Dictionary<string, (PartyResult Result, int Times)> _refusals = [];

        /// <summary>The party's members in join order.</summary>
        public List<string> Joined => Read(() => _party.ConvertAll(member => member.Name));

        /// <summary>The names invited, in order.</summary>
        public List<string> Invited => Read(() => _invited.ToList());

        /// <summary>The members that sent a leave, in order.</summary>
        public List<string> Left => Read(() => _left.ToList());

        public void Add(FakeMember member) => _members.Add(member);

        /// <summary>The next <paramref name="times"/> invites of <paramref name="name"/> are refused with <paramref name="result"/>.</summary>
        public void Refuse(string name, PartyResult result, int times) => _refusals[name] = (result, times);

        /// <summary>The roster a member of the party gets as its character spawns.</summary>
        public void SendRoster(FakeMember to)
        {
            lock (_lock) to.Roster(_party);
        }

        public void Handle(FakeMember from, object message)
        {
            lock (_lock)
            {
                switch (message)
                {
                    case CPartyLeavePacket:
                        _left.Add(from.Name);
                        Leave(from);
                        break;
                    case CPartyInvitePacket invite:
                        _invited.Add(invite.TargetName);
                        if (_refusals.TryGetValue(invite.TargetName, out (PartyResult Result, int Times) refusal) && refusal.Times > 0)
                        {
                            _refusals[invite.TargetName] = (refusal.Result, refusal.Times - 1);
                            from.Result(refusal.Result, invite.TargetName);
                            break;
                        }

                        FakeMember target = _members.Single(member => member.Name == invite.TargetName);
                        _invites[target] = from;
                        target.Invite(from.Name);
                        from.Result(PartyResult.Ok, invite.TargetName);
                        break;
                    case CPartyInviteResponsePacket response:
                        Respond(from, response.Accept);
                        break;
                }
            }
        }

        private void Leave(FakeMember from)
        {
            if (!_party.Remove(from))
            {
                from.Result(PartyResult.NotInParty);
                return;
            }

            from.Roster([]);
            // Fewer than two disbands the party.
            List<FakeMember> rest = _party.Count < 2 ? [] : _party;
            foreach (FakeMember member in _party.ToList()) member.Roster(rest);
            if (rest.Count == 0) _party.Clear();
            from.Result(PartyResult.Ok);
        }

        private void Respond(FakeMember from, bool accept)
        {
            if (!_invites.Remove(from, out FakeMember? inviter))
            {
                from.Result(PartyResult.NoInvite);
                return;
            }

            if (!accept)
            {
                inviter.Result(PartyResult.InviteDeclined, from.Name);
                from.Result(PartyResult.Ok);
                return;
            }

            if (_party.Count == 0) _party.Add(inviter);
            _party.Add(from);
            foreach (FakeMember member in _party) member.Roster(_party);
            from.Result(PartyResult.Ok);
        }

        private T Read<T>(Func<T> read)
        {
            lock (_lock) return read();
        }
    }

    /// <summary>A party member whose requests reach the <see cref="PartyWorld"/> at once, and whose link gets the world's packets.</summary>
    private sealed class FakeMember : IPartyMember
    {
        private readonly PartyWorld _world;
        private readonly PacketCodec _codec;
        private readonly AvalonCryptoSession _server;

        public FakeMember(string name, PartyWorld world)
        {
            Name = name;
            _world = world;
            (_codec, _server) = Session();
            world.Add(this);
        }

        public string Name { get; }

        public bool InWorld => true;

        public int Generation { get; private set; } = 1;

        public PartyLink? Link { get; set; }

        /// <summary>A new connection: what the old one read is no longer the member's.</summary>
        public void Reconnect() => Generation++;

        public ValueTask SendAsync<T>(T message, NetworkPacketType type, CancellationToken ct) where T : class
        {
            _world.Handle(this, message);
            return ValueTask.CompletedTask;
        }

        public void Result(PartyResult result, string? name = null) =>
            Link!.OnPacket(SPartyResultPacket.Create(result, name, _server.Encryptor), _codec, Generation);

        public void Invite(string inviter) =>
            Link!.OnPacket(SPartyInvitePacket.Create(inviter, 1, 1, 60_000, _server.Encryptor), _codec, Generation);

        public void Roster(List<FakeMember> party) =>
            Link!.OnPacket(SPartyRosterPacket.Create(party.Count == 0 ? 0u : 1u, PartyExperienceMode.Even, 0,
                party.ConvertAll(member => new PartyMemberDto { Name = member.Name, Online = true, SameInstance = true }),
                _server.Encryptor), _codec, Generation);
    }

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
