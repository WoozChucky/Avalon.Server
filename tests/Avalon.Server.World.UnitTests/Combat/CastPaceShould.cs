using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Database.World.Seeding;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Abilities;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// #1178, through the real cast handler, cast system and instance: a basic attack is paced by its weapon's attack
/// time over attack speed, a basic spell by its row over cast speed, every other cooldown by its row over cooldown recovery,
/// and every cast time by cast speed. Each is read once, when the cast starts. The global cooldown does not change.
/// Character ids 627_1xx and 1178_4xx are unique to this class.
/// </summary>
public class CastPaceShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private const uint CleaveId = 200;
    private const uint BurstId = 201;

    /// <summary>An instant cone with an 800 ms cooldown, not a basic: its row is paced by cooldown recovery.</summary>
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

    private static DerivedCharacterStats Stats(float castSpeedPct = 0f, float cooldownRecoveryPct = 0f) =>
        new(MaxHealth: 100, MaxPower: 100, Stamina: 0, Strength: 0, Agility: 0, Intellect: 0, Armor: 0,
            BlockPct: 0f, Evasion: 0, CritPct: 0f,
            Offence: new OffenceStats { CastSpeedPct = castSpeedPct, CooldownRecoveryPct = cooldownRecoveryPct });

    private static MapInstanceClient Caster(MapInstance instance, uint id, float castSpeedPct = 0f, float cooldownRecoveryPct = 0f)
    {
        MapInstanceClient caster = Join(instance, id);
        caster.Character.ApplyStats(Stats(castSpeedPct, cooldownRecoveryPct), CurrentValues.EnterWorld, CombatSeed.Formula());
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
        for (int i = 0; i < (int)Math.Ceiling(seconds / s_tick.TotalSeconds); i++)
            instance.Update(s_tick);
    }

    [Theory] // kind, rowCooldownMs, castTimeMs, attack, cast, recovery speed %, attackTimeMs, cooldown s, cast s
    [InlineData("basic attack", 0u, 0u, 20f, 0f, 0f, 792u, 0.66f, 0f)]
    [InlineData("basic attack", 0u, 0u, 80f, 0f, 0f, 900u, 0.6f, 0f)]       // capped at 50
    [InlineData("basic attack", 0u, 0u, -20f, 0f, 0f, 792u, 0.792f, 0f)]    // never lengthens
    [InlineData("basic spell", 800u, 0u, 50f, 25f, 50f, 792u, 0.64f, 0f)]   // D1: cast speed only
    [InlineData("ability", 5000u, 600u, 50f, 50f, 25f, 792u, 4.0f, 0.4f)]   // recovery for the cooldown, cast speed for the cast
    [InlineData("creature basic", 2250u, 0u, 50f, 0f, 0f, 2250u, 1.5f, 0f)]
    public void Pace_each_ability_by_the_rule_its_kind_follows(string kind, uint rowCooldownMs, uint castTimeMs,
        float attackSpeedPct, float castSpeedPct, float cooldownRecoveryPct, uint attackTimeMs, float cooldownSeconds,
        float castSeconds)
    {
        (AbilityFlags flags, AbilityTags tags) = kind switch
        {
            "basic attack" => (AbilityFlags.Basic, AbilityTags.Attack),
            "basic spell" => (AbilityFlags.Basic, AbilityTags.Spell),
            "ability" => (AbilityFlags.None, AbilityTags.Spell),
            _ => (AbilityFlags.None, AbilityTags.Attack),
        };
        var ability = new AbilityMetadata
        {
            Cooldown = rowCooldownMs / 1000f,
            CastTime = castTimeMs / 1000f,
            Flags = flags,
            Tags = tags,
        };
        var offence = new OffenceStats
        {
            AttackSpeedPct = attackSpeedPct,
            CastSpeedPct = castSpeedPct,
            CooldownRecoveryPct = cooldownRecoveryPct,
        };
        var pace = CasterPace.From(offence, attackTimeMs / 1000f, CombatSeed.Formula());

        Assert.Equal(cooldownSeconds, AbilityPace.Cooldown(ability, pace, creatureBasic: kind == "creature basic"), precision: 4);
        Assert.Equal(castSeconds, AbilityPace.CastTime(ability, pace), precision: 4);
    }

    /// <summary>
    /// Review focus 2: a queued basic keeps the pace of the weapon worn when it was queued; the next one follows the weapon
    /// worn then; a running cooldown is never rescaled.
    /// </summary>
    [Fact]
    public void Pace_the_basic_attack_by_the_weapon_worn_when_the_cast_is_queued()
    {
        var warriorOne = new ClassLevelStat
        {
            Class = CharacterClass.Warrior,
            Level = 1,
            BaseHp = 130,
            Stamina = 22,
            Strength = 23,
            Agility = 20,
            Intellect = 20,
        };
        WornItem sword = Weapon(32, attackTimeMs: 792);
        WornItem slow = Weapon(1178_401, attackTimeMs: 900);
        DerivedCharacterStats Wearing(WornItem weapon) => CharacterStatsCalculator.Calculate(warriorOne, [weapon],
            TestCombat.FactorsOf(CharacterClass.Warrior), TestCombat.Formula);

        // A basic attack with a wind-up, so a weapon swap can land while it is queued.
        AbilityTemplate template = AbilityTestData.Cone(CleaveId, reach: 2.5f, arc: 100f);
        template.Flags = AbilityFlags.Basic;
        template.CastTime = 500;
        template.Cooldown = 0;

        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Join(instance, 1178_402);
        warrior.Character.ApplyStats(Wearing(sword), CurrentValues.EnterWorld, TestCombat.Formula);
        warrior.Character.Spells.Load([AbilityTestData.Game(template)]);
        IAbility cleave = Spell(warrior.Character, CleaveId);

        Cast(handler, warrior, CleaveId);
        Assert.True(warrior.Character.Spells.IsCasting);
        warrior.Character.ApplyStats(Wearing(slow), CurrentValues.KeepShare, TestCombat.Formula);
        TickUntilFired(instance, warrior);
        Assert.Equal(0.792 / 1.02, cleave.CooldownTimer, precision: 4);

        float running = cleave.CooldownTimer;
        warrior.Character.ApplyStats(Wearing(sword), CurrentValues.KeepShare, TestCombat.Formula);
        warrior.Character.ApplyStats(Wearing(slow), CurrentValues.KeepShare, TestCombat.Formula);
        Assert.Equal(running, cleave.CooldownTimer);

        cleave.CooldownTimer = 0f;
        Cast(handler, warrior, CleaveId);
        TickUntilFired(instance, warrior);
        Assert.Equal(0.9 / 1.02, cleave.CooldownTimer, precision: 4);
    }

    /// <summary>Ticks until the queued cast has fired: the cooldown it set has not been counted down yet on that tick.</summary>
    private static void TickUntilFired(MapInstance instance, MapInstanceClient caster)
    {
        for (int i = 0; i < 120 && caster.Character.Spells.IsCasting; i++)
            instance.Update(s_tick);
        Assert.False(caster.Character.Spells.IsCasting);
    }

    /// <summary>A sword worn as a copy that rolled 4-7 at <paramref name="attackTimeMs" /> (#1179).</summary>
    private static WornItem Weapon(ulong id, int attackTimeMs) => new(new ItemTemplate
    {
        Id = new ItemTemplateId(id),
        Name = "Sword",
        Class = ItemClass.Weapon,
        SubClass = ItemSubClass.OneHanded,
        Slot = ItemSlotType.MainHand,
        MaxStackSize = 1,
        DamageType1 = DamageType.Physical,
        StatType1 = StatType.Strength,
        StatValue1 = 1,
    }, new ItemBaseStats { DamageMin = 4, DamageMax = 7, AttackTime = attackTimeMs }, null);

    [Fact]
    public void Change_nothing_with_no_speed()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Caster(instance, 627_102);

        Cast(handler, warrior, CleaveId);
        Cast(handler, warrior, BurstId);

        Assert.Equal(0.8f, Spell(warrior.Character, CleaveId).CooldownTimer);
        Assert.Equal(1f, Spell(warrior.Character, BurstId).CastTimeTimer);
    }

    [Fact]
    public void Use_the_cap_for_a_gear_total_above_it()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Caster(instance, 627_103, cooldownRecoveryPct: 80f);

        Cast(handler, warrior, CleaveId);

        Assert.Equal(0.8f / 1.5f, Spell(warrior.Character, CleaveId).CooldownTimer, precision: 5);
    }

    /// <summary>Cast speed shortens the cast and the time every client is told; the cooldown it sets is cooldown recovery's.</summary>
    [Fact]
    public void Shorten_a_queued_casts_time_and_tell_every_client_the_shortened_time()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient wizard = Caster(instance, 627_104, castSpeedPct: 25f);
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
        // Its row's 3 s when it fired, cast speed shortening no cooldown, then counted down by at most a tick since.
        Assert.InRange(Spell(wizard.Character, BurstId).CooldownTimer, 3f - 0.02f, 3f + 1e-4f);
    }

    /// <summary>
    /// A gear change mid-cast changes neither the cast's time nor the cooldown it sets, since both are read once, at cast
    /// start; the next cast uses the new speeds.
    /// </summary>
    [Fact]
    public void Keep_a_started_casts_time_through_a_gear_change_and_use_the_new_speeds_on_the_next_cast()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient wizard = Caster(instance, 627_106);
        IAbility burst = Spell(wizard.Character, BurstId);

        Cast(handler, wizard, BurstId);
        TickFor(instance, 0.5);
        wizard.Character.ApplyStats(Stats(castSpeedPct: 50f, cooldownRecoveryPct: 50f), CurrentValues.KeepShare,
            CombatSeed.Formula());
        float left = burst.CastTimeTimer;
        Assert.InRange(left, 0.45f, 0.55f);   // still counting down the unshortened second

        TickFor(instance, 0.3);
        Assert.True(wizard.Character.Spells.IsCasting);   // a cast at the new cast speed would have fired by now
        TickFor(instance, 0.25);
        Assert.False(wizard.Character.Spells.IsCasting);
        // The cooldown recovery of the cast's start, none: 3 s when it fired, counted down by the few ticks since, where
        // the new recovery would have set 2 s.
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
        MapInstanceClient warrior = Caster(instance, 627_107, cooldownRecoveryPct: 3f);
        IAbility cleave = Spell(warrior.Character, CleaveId);

        Cast(handler, warrior, CleaveId);
        float set = cleave.CooldownTimer;
        warrior.Character.ApplyStats(Stats(cooldownRecoveryPct: 50f), CurrentValues.KeepShare, CombatSeed.Formula());
        warrior.Character.ApplyStats(Stats(), CurrentValues.KeepShare, CombatSeed.Formula());

        Assert.Equal(set, cleave.CooldownTimer);
        Assert.Equal(0.8f / 1.03f, set, precision: 5);
    }

    /// <summary>
    /// #627 review: a cooldown counts down whatever an ability's cast timer holds. Before, it ran only while the
    /// cast timer equalled the metadata's cast time, which a shortened cast time never does.
    /// </summary>
    [Fact]
    public void Count_a_cooldown_down_while_the_cast_timer_holds_a_shortened_time()
    {
        var container = new CharacterAbilityContainer(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        GameAbility burst = AbilityTestData.Game(Burst());
        burst.CastTimeTimer = 1f / 1.25f;   // what a cast at 25 % cast speed sets
        burst.CooldownTimer = 2f;
        container.Load([burst]);

        container.Update(TimeSpan.FromSeconds(0.5));

        Assert.Equal(1.5f, burst.CooldownTimer, precision: 5);
    }

    /// <summary>
    /// #627 review, through the instance: Cleave's cooldown keeps counting down while a shortened cast is under
    /// way, and Burst's own counts down once it fires.
    /// </summary>
    [Fact]
    public void Count_cooldowns_down_during_and_after_a_shortened_cast()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient wizard = Caster(instance, 627_109, castSpeedPct: 25f);
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

    /// <summary>The global cooldown is no speed's: a caster at every cap is refused for the whole 200 ms.</summary>
    [Fact]
    public void Leave_the_global_cooldown_unchanged()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Caster(instance, 627_108, castSpeedPct: 50f, cooldownRecoveryPct: 50f);
        uint gcd = new CombatConfig().GcdMs;

        Cast(handler, warrior, CleaveId);
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = BurstId });

        SAbilityNotReadyPacket refusal = Assert.Single(warrior.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.Equal(CastRejectReason.Gcd, refusal.Reason);
        // A shortened GCD would leave at most 200 / 1.5 = 133 ms; the refusal comes a moment after the cast.
        Assert.InRange(refusal.CooldownMs, (uint)(gcd / 1.5f) + 10, gcd);
    }
}
