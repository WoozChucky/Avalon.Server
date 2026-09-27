using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// #627, through the real cast handler, cast system and instance: a caster's effective haste divides the
/// cooldown a cast sets and the cast time of a queued cast, read once when the cast starts. The global
/// cooldown does not change. Character ids 627_1xx are unique to this class.
/// </summary>
public class CastHasteShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    private const uint CleaveId = 200;
    private const uint BurstId = 201;

    /// <summary>Cleave as seeded: instant, an 800 ms cooldown.</summary>
    private static AbilityTemplate Cleave()
    {
        AbilityTemplate cleave = AbilityTestData.Cone(CleaveId, reach: 2.5f, arc: 100f);
        cleave.Cooldown = 800;
        return cleave;
    }

    /// <summary>A circle with a one-second cast and a three-second cooldown.</summary>
    private static AbilityTemplate Burst()
    {
        AbilityTemplate burst = AbilityTestData.Circle(BurstId);
        burst.CastTime = 1000;
        burst.Cooldown = 3000;
        return burst;
    }

    private static DerivedCharacterStats Stats(float hastePct) =>
        new(MaxHealth: 100, MaxPower: 100, Stamina: 0, Strength: 0, Agility: 0, Intellect: 0, Armor: 0,
            BlockPct: 0f, DodgePct: 0f, CritPct: 0f, AttackDamage: 0, AbilityDamage: 0, HastePct: hastePct);

    private static MapInstanceClient Caster(MapInstance instance, uint id, float hastePct)
    {
        MapInstanceClient caster = Join(instance, id);
        caster.Character.ApplyStats(Stats(hastePct), CurrentValues.EnterWorld, CombatSeed.Formula());
        caster.Character.Spells.Load([AbilityTestData.Game(Cleave()), AbilityTestData.Game(Burst())]);
        return caster;
    }

    private static IAbility Spell(CharacterEntity character, uint id) => character.Spells[new AbilityId(id)]!;

    private static void Cast(CastAbilityHandler handler, MapInstanceClient caster, uint id)
    {
        caster.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-1);   // past the global cooldown
        handler.Execute(caster.Connection, new CCastAbilityPacket { AbilityId = id });
    }

    private static void TickFor(MapInstance instance, double seconds)
    {
        for (int i = 0; i < (int)Math.Ceiling(seconds / Tick.TotalSeconds); i++)
            instance.Update(Tick);
    }

    [Fact]
    public void Set_cleaves_cooldown_to_its_time_over_one_plus_the_haste()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Caster(instance, 627_101, hastePct: 3f);

        Cast(handler, warrior, CleaveId);

        Assert.Equal(3f, warrior.Character.EffectiveHastePct);
        Assert.Equal(0.8f / 1.03f, Spell(warrior.Character, CleaveId).CooldownTimer, precision: 5);
    }

    [Fact]
    public void Change_nothing_with_no_haste()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Caster(instance, 627_102, hastePct: 0f);

        Cast(handler, warrior, CleaveId);
        Cast(handler, warrior, BurstId);

        Assert.Equal(0.8f, Spell(warrior.Character, CleaveId).CooldownTimer);
        Assert.Equal(1f, Spell(warrior.Character, BurstId).CastTimeTimer);
    }

    [Fact]
    public void Use_the_cap_for_a_gear_total_above_it()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Caster(instance, 627_103, hastePct: 80f);

        Cast(handler, warrior, CleaveId);

        Assert.Equal(50f, warrior.Character.EffectiveHastePct);
        Assert.Equal(0.8f / 1.5f, Spell(warrior.Character, CleaveId).CooldownTimer, precision: 5);
    }

    [Fact]
    public void Shorten_a_queued_casts_time_and_tell_every_client_the_shortened_time()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient wizard = Caster(instance, 627_104, hastePct: 25f);
        MapInstanceClient watcher = Join(instance, 627_105);

        Cast(handler, wizard, BurstId);

        Assert.Equal(1f / 1.25f, Spell(wizard.Character, BurstId).CastTimeTimer, precision: 5);
        foreach (MapInstanceClient client in new[] { wizard, watcher })
        {
            SUnitStartCastPacket start = Assert.Single(client.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
            Assert.Equal(1f / 1.25f, start.CastTime, precision: 5);
        }

        TickFor(instance, 0.81);   // 0.8 s, and a tick for the float
        Assert.False(wizard.Character.Spells.IsCasting);
        Assert.Single(wizard.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED));
        // Set to 3 / 1.25 = 2.4 s when it fired, then counted down by at most a tick since.
        Assert.InRange(Spell(wizard.Character, BurstId).CooldownTimer, 2.4f - 0.02f, 2.4f + 1e-4f);
    }

    /// <summary>
    /// Review focus 1: a gear change mid-cast changes neither the cast's time nor the cooldown it sets, since
    /// haste is read once, at cast start; the next cast uses the new haste.
    /// </summary>
    [Fact]
    public void Keep_a_started_casts_time_through_a_gear_change_and_use_the_new_haste_on_the_next_cast()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient wizard = Caster(instance, 627_106, hastePct: 0f);
        IAbility burst = Spell(wizard.Character, BurstId);

        Cast(handler, wizard, BurstId);
        TickFor(instance, 0.5);
        wizard.Character.ApplyStats(Stats(50f), CurrentValues.KeepShare, CombatSeed.Formula());
        float left = burst.CastTimeTimer;
        Assert.InRange(left, 0.45f, 0.55f);   // still counting down the unhasted second

        TickFor(instance, 0.3);
        Assert.True(wizard.Character.Spells.IsCasting);   // a hasted cast would have fired by now
        TickFor(instance, 0.25);
        Assert.False(wizard.Character.Spells.IsCasting);
        // The haste of the cast's start, none: 3 s when it fired, counted down by the few ticks since, where
        // the new haste would have set 2 s.
        Assert.InRange(burst.CooldownTimer, 2.8f, 3f + 1e-4f);

        burst.CooldownTimer = 0f;
        Cast(handler, wizard, BurstId);
        Assert.Equal(1f / 1.5f, burst.CastTimeTimer, precision: 5);
    }

    /// <summary>Review focus 2: a cooldown is scaled once, when it is set; a later gear change leaves a running one alone.</summary>
    [Fact]
    public void Leave_a_running_cooldown_alone_when_the_gear_changes()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Caster(instance, 627_107, hastePct: 3f);
        IAbility cleave = Spell(warrior.Character, CleaveId);

        Cast(handler, warrior, CleaveId);
        float set = cleave.CooldownTimer;
        warrior.Character.ApplyStats(Stats(50f), CurrentValues.KeepShare, CombatSeed.Formula());
        warrior.Character.ApplyStats(Stats(0f), CurrentValues.KeepShare, CombatSeed.Formula());

        Assert.Equal(set, cleave.CooldownTimer);
        Assert.Equal(0.8f / 1.03f, set, precision: 5);
    }

    /// <summary>
    /// #627 review: a cooldown counts down whatever an ability's cast timer holds. Before, it ran only while the
    /// cast timer equalled the metadata's cast time, which a hasted cast time never does.
    /// </summary>
    [Fact]
    public void Count_a_cooldown_down_while_the_cast_timer_holds_a_hasted_time()
    {
        var container = new Avalon.World.Abilities.CharacterAbilityContainer(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var burst = AbilityTestData.Game(Burst());
        burst.CastTimeTimer = 1f / 1.25f;   // what a hasted cast sets
        burst.CooldownTimer = 2f;
        container.Load([burst]);

        container.Update(TimeSpan.FromSeconds(0.5));

        Assert.Equal(1.5f, burst.CooldownTimer, precision: 5);
    }

    /// <summary>
    /// #627 review, through the instance: Cleave's cooldown keeps counting down while a hasted cast is under
    /// way, and Burst's own counts down once it fires.
    /// </summary>
    [Fact]
    public void Count_cooldowns_down_during_and_after_a_hasted_cast()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient wizard = Caster(instance, 627_109, hastePct: 25f);
        IAbility cleave = Spell(wizard.Character, CleaveId);
        IAbility burst = Spell(wizard.Character, BurstId);

        Cast(handler, wizard, CleaveId);
        float cleaveSet = cleave.CooldownTimer;
        Cast(handler, wizard, BurstId);
        TickFor(instance, 0.4);
        Assert.True(wizard.Character.Spells.IsCasting);
        Assert.InRange(cleave.CooldownTimer, cleaveSet - 0.42f, cleaveSet - 0.38f);

        TickFor(instance, 0.45);   // Burst fires at 0.8 s
        Assert.False(wizard.Character.Spells.IsCasting);
        float burstSet = burst.CooldownTimer;
        TickFor(instance, 0.5);
        Assert.InRange(burst.CooldownTimer, burstSet - 0.52f, burstSet - 0.48f);
    }

    /// <summary>The global cooldown is not haste's: a caster at the cap is refused for the whole 200 ms.</summary>
    [Fact]
    public void Leave_the_global_cooldown_unchanged()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Caster(instance, 627_108, hastePct: 50f);
        uint gcd = new CombatConfig().GcdMs;

        Cast(handler, warrior, CleaveId);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = BurstId });

        SAbilityNotReadyPacket refusal = Assert.Single(warrior.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.Equal(CastRejectReason.Gcd, refusal.Reason);
        // A hasted GCD would leave at most 200 / 1.5 = 133 ms; the refusal comes a moment after the cast.
        Assert.InRange(refusal.CooldownMs, (uint)(gcd / 1.5f) + 10, gcd);
    }
}
