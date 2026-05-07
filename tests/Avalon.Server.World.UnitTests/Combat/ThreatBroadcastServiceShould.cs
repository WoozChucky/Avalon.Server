using System.Collections.Generic;
using System.Threading;
using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.World.Combat;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Combat;

public class ThreatBroadcastServiceShould
{
    [Fact]
    public void Should_send_SThreatListPacket_when_target_is_hostile_in_encounter()
    {
        var env = BuildEnvironment();
        var attacker = StubCharacter();
        SeedEncounter(env.combat, attacker, env.hostile, threat: 50f);

        var conn = StubConnection(currentTarget: env.hostileGuid.RawValue);

        env.svc.Tick(new[] { conn }, new Dictionary<ObjectGuid, ICreature> { [env.hostileGuid] = env.hostile }, env.combat);

        conn.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_throttle_rebroadcast_within_interval_and_below_delta()
    {
        // Same threat list across two near-simultaneous ticks → only one packet should fly.
        var env = BuildEnvironment();
        var attacker = StubCharacter();
        SeedEncounter(env.combat, attacker, env.hostile, threat: 50f);

        var conn = StubConnection(currentTarget: env.hostileGuid.RawValue);
        var creatures = new Dictionary<ObjectGuid, ICreature> { [env.hostileGuid] = env.hostile };

        env.svc.Tick(new[] { conn }, creatures, env.combat);
        env.svc.Tick(new[] { conn }, creatures, env.combat);   // immediate re-tick — within 250 ms, 0 % delta

        conn.Received(1).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_resend_when_top_threat_percent_shifts_above_delta()
    {
        // Single hostile with two attackers: shifting their threats flips the top share.
        var env = BuildEnvironment(deltaThreshold: 0.05f);
        var attackerA = StubCharacter();
        var attackerB = StubCharacter();
        SeedEncounter(env.combat, attackerA, env.hostile, threat: 100f);
        SeedEncounter(env.combat, attackerB, env.hostile, threat: 1f);   // A is the top by ~99 %

        var conn = StubConnection(currentTarget: env.hostileGuid.RawValue);
        var creatures = new Dictionary<ObjectGuid, ICreature> { [env.hostileGuid] = env.hostile };

        env.svc.Tick(new[] { conn }, creatures, env.combat);   // baseline — first send

        // Flip the lead: B now massively outthreats A.
        var encounter = (Encounter)System.Linq.Enumerable.First(env.registry.Active);
        encounter.AddThreat(env.hostile, attackerB, 1000f);

        env.svc.Tick(new[] { conn }, creatures, env.combat);   // top-share moved by ~80 % → resend

        conn.Received(2).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_resend_after_throttle_interval_elapses()
    {
        var env = BuildEnvironment(intervalMs: 25);   // tight interval so the test runs fast
        var attacker = StubCharacter();
        SeedEncounter(env.combat, attacker, env.hostile, threat: 50f);

        var conn = StubConnection(currentTarget: env.hostileGuid.RawValue);
        var creatures = new Dictionary<ObjectGuid, ICreature> { [env.hostileGuid] = env.hostile };

        env.svc.Tick(new[] { conn }, creatures, env.combat);
        Thread.Sleep(35);    // exceed configured 25 ms throttle window
        env.svc.Tick(new[] { conn }, creatures, env.combat);

        conn.Received(2).Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_not_send_when_no_current_target()
    {
        var env = BuildEnvironment();
        var attacker = StubCharacter();
        SeedEncounter(env.combat, attacker, env.hostile, threat: 50f);

        var conn = StubConnection(currentTarget: null);

        env.svc.Tick(new[] { conn }, new Dictionary<ObjectGuid, ICreature> { [env.hostileGuid] = env.hostile }, env.combat);

        conn.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_not_send_when_target_creature_missing_from_map()
    {
        var env = BuildEnvironment();
        // Note: creatures dict is empty — the creature has despawned but the client still
        // holds a stale target guid. Service must skip cleanly and clear its internal state.
        var conn = StubConnection(currentTarget: env.hostileGuid.RawValue);

        env.svc.Tick(new[] { conn }, new Dictionary<ObjectGuid, ICreature>(), env.combat);

        conn.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_not_send_when_target_is_not_in_an_encounter()
    {
        // Hostile creature is alive on the map but not currently engaged with anyone.
        var env = BuildEnvironment();

        var conn = StubConnection(currentTarget: env.hostileGuid.RawValue);

        env.svc.Tick(new[] { conn }, new Dictionary<ObjectGuid, ICreature> { [env.hostileGuid] = env.hostile }, env.combat);

        conn.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_not_send_when_character_is_dead()
    {
        var env = BuildEnvironment();
        var attacker = StubCharacter();
        SeedEncounter(env.combat, attacker, env.hostile, threat: 50f);

        var conn = StubConnection(currentTarget: env.hostileGuid.RawValue);
        conn.Character!.IsDead.Returns(true);

        env.svc.Tick(new[] { conn }, new Dictionary<ObjectGuid, ICreature> { [env.hostileGuid] = env.hostile }, env.combat);

        conn.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    [Fact]
    public void Should_resend_when_target_changes_to_a_different_creature()
    {
        // Same connection, two hostiles. Switching target should bypass the throttle window.
        var env = BuildEnvironment();
        ulong hostile2Raw = (((ulong)ObjectType.Creature) << 56) | 999u;
        var hostile2Guid = new ObjectGuid(hostile2Raw);
        var hostile2 = Substitute.For<ICreature>();
        hostile2.Guid.Returns(hostile2Guid);

        var attacker = StubCharacter();
        SeedEncounter(env.combat, attacker, env.hostile, threat: 50f);
        SeedEncounter(env.combat, attacker, hostile2, threat: 70f);

        var conn = StubConnection(currentTarget: env.hostileGuid.RawValue);
        var creatures = new Dictionary<ObjectGuid, ICreature>
        {
            [env.hostileGuid] = env.hostile,
            [hostile2Guid]    = hostile2,
        };

        env.svc.Tick(new[] { conn }, creatures, env.combat);
        // Client picks a different mob — same tick interval, but target changed, so we must resend.
        conn.CurrentTargetGuid.Returns(hostile2Raw);
        env.svc.Tick(new[] { conn }, creatures, env.combat);

        conn.Received(2).Send(Arg.Any<NetworkPacket>());
    }

    // ---------- helpers ----------

    private sealed record TestEnv(
        ThreatBroadcastService svc,
        ICombatService          combat,
        EncounterRegistry       registry,
        ObjectGuid              hostileGuid,
        ICreature               hostile);

    private static TestEnv BuildEnvironment(uint intervalMs = 250, float deltaThreshold = 0.05f)
    {
        var cfg = new CombatConfig
        {
            InitialThreatSeed             = 0f,
            ThreatBroadcastIntervalMs     = intervalMs,
            ThreatBroadcastDeltaThreshold = deltaThreshold,
        };
        var registry = new EncounterRegistry(cfg);
        var ctx      = Substitute.For<Avalon.World.Public.Instances.ISimulationContext>();
        var combat   = new CombatService(cfg, registry, ctx);

        var svc = new ThreatBroadcastService(cfg);

        var hostileGuid = new ObjectGuid(ObjectType.Creature, 1);
        var hostile = Substitute.For<ICreature>();
        hostile.Guid.Returns(hostileGuid);
        // CurrentHealth>0 prevents accidental death-detection paths inside CombatService.
        hostile.CurrentHealth.Returns(100u);

        return new TestEnv(svc, combat, registry, hostileGuid, hostile);
    }

    /// <summary>
    /// Adds <paramref name="attacker"/> and <paramref name="hostile"/> to a shared encounter
    /// then bumps the threat to a known value. We bypass <c>CombatService.ApplyDamage</c> so
    /// we control threat shape independently of the class-baseline / ability-multiplier math.
    /// </summary>
    private static void SeedEncounter(ICombatService combat, ICharacter attacker, ICreature hostile, float threat)
    {
        combat.EnterCombat(hostile, attacker);
        var enc = (Encounter)combat.GetEncounterFor(hostile)!;
        enc.AddThreat(hostile, attacker, threat);
    }

    private static ICharacter StubCharacter()
    {
        var c = Substitute.For<ICharacter>();
        c.CurrentHealth.Returns(100u);
        c.Guid.Returns(new ObjectGuid(ObjectType.Character, (uint)(System.Threading.Interlocked.Increment(ref s_charSeq))));
        return c;
    }

    private static int s_charSeq;

    private static IWorldConnection StubConnection(ulong? currentTarget)
    {
        var conn = Substitute.For<IWorldConnection>();
        var character = Substitute.For<ICharacter>();
        character.IsDead.Returns(false);
        conn.Character.Returns(character);
        conn.CurrentTargetGuid.Returns(currentTarget);
        conn.CryptoSession.Returns(new FakeAvalonCryptoSession());
        return conn;
    }

}
