using System.Runtime.CompilerServices;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Scripts;
using Avalon.World.Abilities;
using Avalon.World.Auras;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>RecordingAbilityScript's statics are shared, so the classes reading them never run in parallel.</summary>
[CollectionDefinition(nameof(RecordingAbilityScript), DisableParallelization = true)]
public sealed class RecordingAbilityScriptCollection;

[Collection(nameof(RecordingAbilityScript))]
public class InstanceAbilityCastSystemShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly AbilityAim s_aim = new(new Vector3(0f, 0f, 1f), new Vector3(3f, 0f, 4f));

    /// <summary>The aim a script gets: <see cref="s_aim" /> with the caster's position at cast start as its origin (#648).</summary>
    private static readonly AbilityAim s_taken = s_aim with { Origin = Vector3.zero };

    private readonly IScriptManager _scripts = Substitute.For<IScriptManager>();
    private readonly IAbilityArena _arena = Substitute.For<IAbilityArena>();
    private readonly InstanceAbilityCastSystem _sut;

    public InstanceAbilityCastSystemShould()
    {
        RecordingAbilityScript.Prepared.Clear();
        RecordingAbilityScript.LastBuilt = null;
        _scripts.GetAbilityScript("Recording").Returns(typeof(RecordingAbilityScript));
        _sut = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, Substitute.For<IServiceProvider>(), _scripts, _arena);
    }

    /// <summary>
    /// A cast taken ends arrival protection (#1088) before it is sent or fires; a refused one keeps it.
    /// </summary>
    [Theory]
    [InlineData(0f, 0u)]
    [InlineData(1f, 0u)]
    [InlineData(0f, 30u)]   // refused: the cost cannot be paid
    public void End_arrival_protection_only_when_a_cast_is_taken(float castTime, uint cost)
    {
        CharacterEntity caster = GodCaster(PowerType.Mana);
        caster.GodMode = false;
        ArrivalProtectionTestData.Apply(caster);
        GameAbility ability = Ability(cost, castTime);
        bool? protectedWhenSent = null;
        _arena.When(a => a.BroadcastUnitStartCast(caster, ability, Arg.Any<uint>(), Arg.Any<AbilityFootprint?>()))
            .Do(_ => protectedWhenSent = ArrivalProtection.IsProtected(caster));
        _arena.When(a => a.BroadcastFinishCast(caster, ability, Arg.Any<uint>()))
            .Do(_ => protectedWhenSent = ArrivalProtection.IsProtected(caster));

        bool accepted = castTime > 0
            ? _sut.QueueAbility(caster, s_aim, ability)
            : _sut.RunInstant(caster, s_aim, ability);

        Assert.Equal(cost == 0, accepted);
        Assert.Equal(accepted ? false : null, protectedWhenSent);
        Assert.Equal(!accepted, ArrivalProtection.IsProtected(caster));
    }

    /// <summary>
    /// An ability whose cost, if any, is spent from mana (#1181); with <paramref name="projectiles" />, a projectile's
    /// shape firing that many (#1181 Part D, a fan).
    /// </summary>
    private static GameAbility Ability(uint cost = 0, float castTime = 0f, string script = "Recording",
        AbilityFlags flags = AbilityFlags.None, int projectiles = 0) => new()
        {
            AbilityId = new AbilityId(1),
            Metadata = new AbilityMetadata
            {
                Name = "x",
                ScriptName = script,
                Cost = cost,
                CastTime = castTime,
                Cooldown = 2f,
                Flags = flags,
                Shape = projectiles > 0 ? AbilityShape.Projectile : AbilityShape.Circle,
                ProjectileCount = Math.Max(1, projectiles),
            },
            CastTimeTimer = castTime,
            CooldownTimer = 0f,
        };

    private static ICharacter Caster(PowerType type = PowerType.Mana, uint power = 100)
    {
        ICharacter character = Substitute.For<ICharacter>();
        character.PowerType.Returns(type);
        character.CurrentPower.Returns(power);
        character.Position.Returns(Vector3.zero);
        return character;
    }

    private static CharacterEntity GodCaster(PowerType type = PowerType.None, uint power = 0)
    {
        CharacterEntity character = TestCharacters.New(628);
        character.Health = 100;
        character.CurrentHealth = 100;
        character.PowerType = type;
        character.CurrentPower = power;
        character.GodMode = true;
        return character;
    }

    /// <summary>
    /// The cast system refuses a stunned or silenced caster itself (#770), whichever path the cast came by, so nothing
    /// bypasses the handlers' gates: a stun refuses every cast, an item's included; a silence every one but a basic
    /// attack (a class basic by its row's flag, a creature's own basic) and a cast an item started (an item use), on
    /// either path. A root refuses none. A refused cast spends nothing and starts nothing.
    /// </summary>
    [Theory]
    [InlineData("spell", CrowdControl.Stun, false, false)]
    [InlineData("basic", CrowdControl.Stun, true, false)]
    [InlineData("item", CrowdControl.Stun, true, false)]
    [InlineData("spell", CrowdControl.Silence, false, false)]
    [InlineData("spell", CrowdControl.Silence, true, false)]
    [InlineData("basic", CrowdControl.Silence, true, true)]
    [InlineData("item", CrowdControl.Silence, false, true)]
    [InlineData("item", CrowdControl.Silence, true, true)]
    [InlineData("creature basic", CrowdControl.Silence, false, true)]
    [InlineData("creature special", CrowdControl.Silence, false, false)]
    [InlineData("spell", CrowdControl.Root, true, true)]
    public void Refuse_a_controlled_caster_unless_its_cast_is_spared(string cast, CrowdControl control, bool queued,
        bool taken)
    {
        IUnit caster;
        IAbility ability;
        if (cast.StartsWith("creature", StringComparison.Ordinal))
        {
            _scripts.GetAbilityScript(Arg.Any<string>()).Returns(typeof(RecordingAbilityScript));
            AbilityTemplate basic = AbilityTestData.Cone(770_001);
            AbilityTemplate special = AbilityTestData.Cone(770_002);
            var boar = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, 770_901),
                Metadata = Loot.LootTestData.BoarTemplate(null),
                Name = "Boar",
                Health = 100,
                CurrentHealth = 100,
            };
            boar.Abilities.Load(Creatures.CreatureAbilitiesShould.Catalog(basic, special),
                new CreatureAbilityKit(basic.Id, special.Id), NullLogger.Instance, boar.Name);
            boar.Auras.Add(AuraTestData.HeldControl(control), DateTimeOffset.UnixEpoch);
            caster = boar;
            ability = boar.Abilities[cast == "creature basic" ? basic.Id : special.Id]!;
        }
        else
        {
            CharacterEntity character = GodCaster();
            character.GodMode = false;
            character.Auras.Add(AuraTestData.HeldControl(control), DateTimeOffset.UnixEpoch);
            caster = character;
            AbilityFlags flags = cast == "basic" ? AbilityFlags.Basic : AbilityFlags.None;
            ability = Ability(castTime: queued ? 1f : 0f, flags: flags);
        }

        bool item = cast == "item";
        bool accepted = queued
            ? _sut.QueueAbility(caster, s_aim, ability, free: item, forItem: item)
            : _sut.RunInstant(caster, s_aim, ability, free: item, forItem: item);

        Assert.Equal(taken, accepted);
        Assert.Equal(taken && queued, ability.Casting);
        Assert.Equal(taken && !queued ? 1 : 0, RecordingAbilityScript.Prepared.Count);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.01f)]
    public void Cast_for_free_without_a_power_pool_on_both_paths_in_god_mode(float castTime)
    {
        CharacterEntity caster = GodCaster();
        GameAbility ability = Ability(cost: 30, castTime: castTime);

        bool accepted = castTime > 0 ? _sut.QueueAbility(caster, s_aim, ability) : _sut.RunInstant(caster, s_aim, ability);
        if (castTime > 0) _sut.Update(s_tick, []);

        Assert.True(accepted);
        Assert.Equal(0u, caster.CurrentPower);
        Assert.Equal(0f, ability.CooldownTimer);
        Assert.Single(RecordingAbilityScript.Prepared);
    }

    [Fact]
    public void Keep_an_old_cooldown_ticking_without_replacing_it_during_god_mode()
    {
        CharacterEntity caster = GodCaster();
        GameAbility ability = Ability();
        ability.CooldownTimer = 5f;
        caster.Spells.Load([ability]);

        caster.Spells.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(4f, ability.CooldownTimer);
        Assert.True(_sut.RunInstant(caster, s_aim, ability));
        Assert.Equal(4f, ability.CooldownTimer);
    }

    [Fact]
    public void Start_a_cooldown_at_fire_if_god_mode_was_disabled_during_a_free_queued_cast()
    {
        CharacterEntity caster = GodCaster(PowerType.Mana);
        GameAbility ability = Ability(cost: 30, castTime: 0.01f);

        Assert.True(_sut.QueueAbility(caster, s_aim, ability));
        caster.GodMode = false;
        _sut.Update(s_tick, []);

        Assert.Equal(0u, caster.CurrentPower);
        Assert.Equal(2f, ability.CooldownTimer);
        Assert.Single(RecordingAbilityScript.Prepared);
    }

    [Fact]
    public void Keep_a_paid_cost_but_skip_cooldown_if_god_mode_was_enabled_during_a_queued_cast()
    {
        CharacterEntity caster = GodCaster(PowerType.Mana, 40);
        caster.GodMode = false;
        GameAbility ability = Ability(cost: 30, castTime: 0.01f);

        Assert.True(_sut.QueueAbility(caster, s_aim, ability));
        Assert.Equal(10u, caster.CurrentPower);
        caster.GodMode = true;
        _sut.Update(s_tick, []);

        Assert.Equal(10u, caster.CurrentPower);
        Assert.Equal(0f, ability.CooldownTimer);
    }

    // ── #521 item 1: Casting is set only once the queue took the cast ──

    [Fact]
    public void Leave_Casting_clear_when_the_queue_refuses_the_cast()
    {
        GameAbility ability = Ability(cost: 30, castTime: 1f);

        bool queued = _sut.QueueAbility(Caster(PowerType.None), s_aim, ability);

        Assert.False(queued);
        Assert.False(ability.Casting);
    }

    [Fact]
    public void Set_Casting_and_pay_once_the_cast_is_queued()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30, castTime: 1f);

        Assert.True(_sut.QueueAbility(caster, s_aim, ability));

        Assert.True(ability.Casting);
        caster.Received(1).CurrentPower = 70u;
    }

    /// <summary>A queued cast whose script cannot be found spends nothing and does not start casting.</summary>
    [Fact]
    public void Queue_nothing_and_spend_nothing_when_the_script_is_missing()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30, castTime: 1f, script: "Nope");

        Assert.False(_sut.QueueAbility(caster, s_aim, ability));

        Assert.False(ability.Casting);
        caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
        Assert.Equal(0f, ability.CooldownTimer);
    }

    /// <summary>
    /// A queued cast's script is built when it is queued, so one that cannot be built is refused
    /// before anything is spent, not found out at completion after the cost was paid.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Spend_nothing_on_a_script_whose_constructor_throws_on_both_paths(float castTime)
    {
        _scripts.GetAbilityScript("Throwing").Returns(typeof(ThrowingAbilityScript));
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30, castTime: castTime, script: "Throwing");

        bool accepted = castTime > 0 ? _sut.QueueAbility(caster, s_aim, ability) : _sut.RunInstant(caster, s_aim, ability);

        Assert.False(accepted);
        Assert.False(ability.Casting);
        caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
        Assert.Equal(0f, ability.CooldownTimer);
    }

    /// <summary>
    /// A script removed after the cast was queued (a reload) does not cost the cast: the script was
    /// built when the cast was queued, and that one fires.
    /// </summary>
    [Fact]
    public void Fire_the_script_built_at_queue_time_even_if_the_script_is_gone_at_completion()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30, castTime: 0.01f);
        Assert.True(_sut.QueueAbility(caster, s_aim, ability));
        _scripts.GetAbilityScript("Recording").Returns((Type?)null);

        _sut.Update(s_tick, []);

        Assert.Equal(s_taken, Assert.Single(RecordingAbilityScript.Prepared).Aim);
        caster.Received(1).CurrentPower = 70u;
        Assert.Equal(2f, ability.CooldownTimer);
    }

    /// <summary>
    /// The script manager is asked for the type on every cast, and a type's factory is kept by the type, not by the
    /// name (#925): once a name answers with another type (a reload), the next cast builds that one.
    /// </summary>
    [Fact]
    public void Build_the_type_a_script_name_answers_with_now_not_the_one_it_built_before()
    {
        ICharacter caster = Caster();
        Assert.True(_sut.RunInstant(caster, s_aim, Ability()));
        _scripts.GetAbilityScript("Recording").Returns(typeof(UpdateThrowingAbilityScript));

        Assert.True(_sut.RunInstant(caster, s_aim, Ability()));

        List<IWorldObject> objects = [];
        _sut.Update(s_tick, objects);
        Assert.IsType<UpdateThrowingAbilityScript>(Assert.Single(objects));
        Assert.Single(RecordingAbilityScript.Prepared);
    }

    /// <summary>
    /// Every cast's aim is handed to its script's constructor out of one box the cast system keeps and overwrites
    /// (#925). A script must keep the aim it was built with: the first cast here is still winding up, its script
    /// already built, when the second cast's aim goes into the box. The second has a fan's shape (#1181 Part D, three
    /// projectiles) on a script that flies no fans, so it fires its one lane along its own aim, never turned.
    /// </summary>
    [Fact]
    public void Fire_each_script_with_the_aim_of_its_own_cast()
    {
        var other = new AbilityAim(new Vector3(1f, 0f, 0f), new Vector3(-7f, 0f, 2f));
        ICharacter first = Caster();
        ICharacter second = Caster();
        Assert.True(_sut.QueueAbility(first, s_aim, Ability(castTime: 0.01f)));
        Assert.True(_sut.RunInstant(second, other, Ability(projectiles: 3)));

        _sut.Update(s_tick, []);

        Assert.Equal(2, RecordingAbilityScript.Prepared.Count);
        Assert.Equal(other with { Origin = Vector3.zero }, RecordingAbilityScript.Prepared[0].Aim);
        Assert.Equal(s_taken, RecordingAbilityScript.Prepared[1].Aim);
        Assert.Same(first, RecordingAbilityScript.Prepared[1].Caster);
    }

    /// <summary>
    /// A script is handed a copy of its caster's ability, which shares only the ability's metadata (#925): whatever
    /// the script writes to its copy, the caster's cooldown, cast timer and casting flag stay the cast system's.
    /// </summary>
    [Fact]
    public void Keep_the_casters_cooldown_and_cast_state_out_of_its_scripts_reach()
    {
        GameAbility ability = Ability(castTime: 1f);
        Assert.True(_sut.RunInstant(Caster(), s_aim, ability));
        IAbility held = RecordingAbilityScript.LastBuilt!.Held;

        held.CooldownTimer = 0f;
        held.CastTimeTimer = 0f;
        held.Casting = true;

        Assert.NotSame(ability, held);
        Assert.Equal(ability.AbilityId, held.AbilityId);
        Assert.Equal(2f, ability.CooldownTimer);
        Assert.Equal(1f, ability.CastTimeTimer);
        Assert.False(ability.Casting);
    }

    // ── #521 item 2: one power rule on both paths ──

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Refuse_a_costed_cast_by_a_caster_without_a_pool_on_both_paths(float castTime)
    {
        ICharacter caster = Caster(PowerType.None);
        GameAbility ability = Ability(cost: 30, castTime: castTime);

        bool accepted = castTime > 0 ? _sut.QueueAbility(caster, s_aim, ability) : _sut.RunInstant(caster, s_aim, ability);

        Assert.False(accepted);
        caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
        Assert.Equal(0f, ability.CooldownTimer);
    }

    /// <summary>
    /// #1181: a cast the caster's mana can pay is taken and charged on both paths; one it cannot pay is refused before
    /// anything is built, paid or marked casting.
    /// </summary>
    [Theory]
    [InlineData(0f, 30u, true)]
    [InlineData(0f, 19u, false)]
    [InlineData(1.5f, 30u, true)]
    [InlineData(1.5f, 19u, false)]
    public void Take_a_cast_the_mana_can_pay_and_refuse_one_it_cannot_on_both_paths(float castTime, uint mana, bool taken)
    {
        ICharacter caster = Caster(PowerType.Mana, power: mana);
        GameAbility ability = Ability(cost: 20, castTime: castTime);

        bool accepted = castTime > 0 ? _sut.QueueAbility(caster, s_aim, ability) : _sut.RunInstant(caster, s_aim, ability);

        Assert.Equal(taken, accepted);
        if (taken)
        {
            caster.Received(1).CurrentPower = mana - 20u;
        }
        else
        {
            Assert.False(ability.Casting);
            caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
            Assert.Empty(RecordingAbilityScript.Prepared);
        }
    }

    [Fact]
    public void Pay_start_the_cooldown_and_fire_an_instant_cast_with_its_aim()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30);

        Assert.True(_sut.RunInstant(caster, s_aim, ability));

        caster.Received(1).CurrentPower = 70u;
        Assert.Equal(2f, ability.CooldownTimer);
        _arena.Received(1).BroadcastFinishCast(caster, ability, Arg.Any<uint>());
        (IUnit who, AbilityAim aim, IAbilityArena arena) = Assert.Single(RecordingAbilityScript.Prepared);
        Assert.Same(caster, who);
        Assert.Equal(s_taken, aim);
        Assert.Same(_arena, arena);
    }

    [Fact]
    public void Spend_nothing_when_the_script_is_missing()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30, script: "Nope");

        Assert.False(_sut.RunInstant(caster, s_aim, ability));

        caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
        Assert.Equal(0f, ability.CooldownTimer);
    }

    // ── the aim captured at cast start is the one the script gets ──

    [Fact]
    public void Fire_a_queued_cast_with_the_aim_it_was_queued_with()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(castTime: 0.01f);
        _sut.QueueAbility(caster, s_aim, ability);

        _sut.Update(s_tick, []);

        Assert.Equal(s_taken, Assert.Single(RecordingAbilityScript.Prepared).Aim);
        Assert.False(ability.Casting);
        Assert.Equal(2f, ability.CooldownTimer);
    }

    // ── #521 item 3: finished and interrupted entries are removed after the loop ──

    [Fact]
    public void Complete_one_cast_and_interrupt_another_in_the_same_update()
    {
        ICharacter still = Caster();
        ICharacter mover = Caster();
        GameAbility finishing = Ability(castTime: 0.01f);
        GameAbility interrupted = Ability(castTime: 5f);
        _sut.QueueAbility(still, s_aim, finishing);
        _sut.QueueAbility(mover, s_aim, interrupted);
        mover.Position.Returns(new Vector3(1f, 0f, 0f));

        _sut.Update(s_tick, []);
        _sut.Update(s_tick, []);

        Assert.Single(RecordingAbilityScript.Prepared);
        Assert.False(finishing.Casting);
        Assert.False(interrupted.Casting);
        _arena.Received(1).BroadcastInterruptedCast(mover, interrupted, Arg.Any<uint>());
    }

    // ── a caster leaving the instance takes no cast with it (#164) ──

    /// <summary>
    /// Cancelling one caster's casts clears only that caster's: its timers reset, Casting clears, the
    /// interrupt is sent, nothing fires and nothing is refunded; another caster's cast still completes.
    /// </summary>
    [Fact]
    public void Cancel_only_the_given_casters_queued_casts()
    {
        ICharacter leaving = Caster();
        ICharacter staying = Caster();
        GameAbility cancelled = Ability(cost: 30, castTime: 0.6f);
        GameAbility kept = Ability(castTime: 0.6f);
        Assert.True(_sut.QueueAbility(leaving, s_aim, cancelled));
        Assert.True(_sut.QueueAbility(staying, s_aim, kept));
        _sut.Update(s_tick, []);

        _sut.CancelCasts(leaving);

        Assert.False(cancelled.Casting);
        Assert.Equal(0.6f, cancelled.CastTimeTimer);
        _arena.Received(1).BroadcastInterruptedCast(leaving, cancelled, Arg.Any<uint>());
        leaving.Received(1).CurrentPower = 70u;
        Assert.True(kept.Casting);
        _arena.DidNotReceive().BroadcastInterruptedCast(staying, Arg.Any<IAbility>(), Arg.Any<uint>());
        Assert.Empty(RecordingAbilityScript.Prepared);

        for (int i = 0; i < 40; i++)
        {
            _sut.Update(s_tick, []);
        }

        (IUnit who, _, _) = Assert.Single(RecordingAbilityScript.Prepared);
        Assert.Same(staying, who);
        _arena.DidNotReceive().BroadcastFinishCast(leaving, Arg.Any<IAbility>(), Arg.Any<uint>());
        Assert.Equal(0f, cancelled.CooldownTimer);
    }

    // ── a dead caster's cast is dropped at completion ──

    [Fact]
    public void Drop_a_cast_whose_caster_died_before_it_completed_and_take_the_next_one()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(castTime: 0.01f);
        _sut.QueueAbility(caster, s_aim, ability);
        caster.IsDead.Returns(true);

        _sut.Update(s_tick, []);

        Assert.Empty(RecordingAbilityScript.Prepared);
        Assert.False(ability.Casting);
        _arena.DidNotReceive().BroadcastFinishCast(caster, Arg.Any<IAbility>(), Arg.Any<uint>());

        // Nothing is left behind that would refuse the next cast: the queue takes it again.
        caster.IsDead.Returns(false);
        Assert.True(_sut.QueueAbility(caster, s_aim, ability));
        _sut.Update(s_tick, []);
        Assert.Single(RecordingAbilityScript.Prepared);
    }

    /// <summary>A script that finished in Prepare (a circle, a cone) never enters the active list.</summary>
    [Fact]
    public void Keep_no_finished_script_in_the_active_list()
    {
        _sut.RunInstant(Caster(), s_aim, Ability());

        Assert.NotNull(RecordingAbilityScript.LastBuilt);
        Assert.Null(_sut.GetAbility(RecordingAbilityScript.LastBuilt!.Guid));
    }

    /// <summary>A skill that affects nobody still spends its cost and starts its cooldown (#164).</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Spend_the_cost_and_start_the_cooldown_of_a_cast_that_hits_nobody_on_both_paths(float castTime)
    {
        var arena = new TestArena();
        _scripts.GetAbilityScript(nameof(CircleAbilityScript)).Returns(typeof(CircleAbilityScript));
        var sut = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, Substitute.For<IServiceProvider>(), _scripts, arena);
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30, castTime: castTime, script: nameof(CircleAbilityScript));

        bool accepted = castTime > 0 ? sut.QueueAbility(caster, s_aim, ability) : sut.RunInstant(caster, s_aim, ability);
        for (int i = 0; castTime > 0 && i < 61; i++)
        {
            sut.Update(s_tick, []);
        }

        Assert.True(accepted);
        Assert.Single(arena.Fired);
        Assert.Empty(arena.Damaged());
        caster.Received(1).CurrentPower = 70u;
        Assert.Equal(2f, ability.CooldownTimer);
    }

    /// <summary>
    /// A projectile that ends stays a world object until its final state has been taken for the next
    /// broadcast, so every client sees where it stopped; it is not ticked again, and the update after
    /// that despawns it (#164).
    /// </summary>
    [Fact]
    public void Keep_a_finished_projectile_until_its_final_state_is_taken_then_despawn_it()
    {
        var arena = new TestArena();
        _scripts.GetAbilityScript(nameof(ProjectileAbilityScript)).Returns(typeof(ProjectileAbilityScript));
        var sut = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, Substitute.For<IServiceProvider>(), _scripts, arena);
        arena.Creature(0f, 1f);
        GameAbility ability = AbilityTestData.Game(AbilityTestData.Projectile(1, reach: 5f, speed: 20f));
        Assert.True(sut.RunInstant(arena.Player(1, 0f, 0f), new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 5f)), ability));

        List<IWorldObject> objects = [];
        for (int i = 0; i < 3; i++)
        {
            objects.Clear();
            sut.Update(s_tick, objects);
        }

        AbilityScript projectile = Assert.IsAssignableFrom<AbilityScript>(Assert.Single(objects));
        Assert.Equal(SpellState.Finished, projectile.State);
        Assert.Single(arena.Damaged());

        // Its final state is still owed to the clients: it stays.
        objects.Clear();
        sut.Update(s_tick, objects);
        Assert.Same(projectile, Assert.Single(objects));

        Assert.NotEqual(GameEntityFields.None, projectile.ConsumeDirtyFields());
        objects.Clear();
        sut.Update(s_tick, objects);

        Assert.Empty(objects);
        Assert.Null(sut.GetAbility(projectile.Guid));
        Assert.Single(arena.Damaged());
    }

    private static readonly AbilityAim s_north = new(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 10f));

    /// <summary>
    /// Projectiles are pooled per instance (#1181 Part D): one that left the world is reused only once two whole view
    /// rounds have passed, when no view can still hold its guid's wrapper, with a new id in the same wrapper, and nothing
    /// of its last flight: not its hit set, caster, ability, cast id, lane or state. A queued cast that never fired hands
    /// its projectile back at once, since nothing saw it. A projectile that threw is never reused, the pool keeps at
    /// most its cap, and a pooled projectile keeps neither its caster nor its metadata alive. A piercing fan's lane
    /// that ends first goes back alone, leaving the cast's shared set of hit units to the lanes still flying, each
    /// counting its own hits for MaxTargets. Each round casts at a creature 6 m north and flies the projectile to its
    /// end, its changes taken as the broadcast takes them.
    /// </summary>
    [Theory]
    [InlineData("after two view rounds")]
    [InlineData("after one view round")]
    [InlineData("nothing of its last flight")]
    [InlineData("a queued cast that never fired")]
    [InlineData("after its flight threw")]
    [InlineData("past the pool's capacity")]
    [InlineData("keeps nothing alive")]
    [InlineData("a piercing lane that ends first")]
    public void Reuse_a_projectile_only_once_nothing_can_still_see_its_last_flight(string row)
    {
        var arena = new TestArena();
        _scripts.GetAbilityScript(nameof(ProjectileAbilityScript)).Returns(typeof(ProjectileAbilityScript));
        var sut = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, Substitute.For<IServiceProvider>(), _scripts, arena);
        CharacterEntity caster = arena.Player(1, 0f, 0f);
        ICreature creature = arena.Creature(0f, 6f, health: 1000);
        AbilityTemplate template = AbilityTestData.Projectile(219, reach: 10f, speed: 20f).Dealing(10);
        GameAbility ability = AbilityTestData.Game(template);
        List<IWorldObject> objects = [];

        ProjectileAbilityScript Round(IUnit by, GameAbility cast)
        {
            Assert.True(sut.RunInstant(by, s_north, cast));
            return Assert.Single(Fly(sut, objects));
        }

        switch (row)
        {
            case "after two view rounds":
            case "after one view round":
            {
                ProjectileAbilityScript first = Round(caster, ability);
                ObjectGuid wrapper = first.Guid;
                ulong firstId = wrapper.RawValue;
                arena.ViewRound += row == "after two view rounds" ? 2u : 1u;

                ProjectileAbilityScript second = Round(caster, ability);

                if (row == "after one view round")
                {
                    Assert.NotSame(first, second);
                    Assert.Equal(firstId, first.Guid.RawValue);
                    break;
                }

                Assert.Same(first, second);
                Assert.Same(wrapper, second.Guid);
                Assert.NotEqual(firstId, second.Guid.RawValue);
                Assert.Equal(ObjectType.SpellProjectile, second.Guid.Type);
                Assert.Equal([creature, creature], arena.Damaged());
                break;
            }

            case "nothing of its last flight":
            {
                ProjectileAbilityScript first = Round(caster, ability);
                arena.ViewRound += 2;
                CharacterEntity other = arena.Player(2, 3f, 0f);
                int fired = arena.FiredFootprints.Count;

                Assert.True(sut.RunInstant(other, s_north,
                    AbilityTestData.Game(AbilityTestData.Projectile(221, reach: 10f, speed: 20f, pierce: true))));

                var identity = (IProjectileIdentity)first;
                Assert.Equal(other.Guid.RawValue, identity.CasterGuid);
                Assert.Equal(221u, identity.AbilityId);
                Assert.Equal(arena.FinishedIds[^1], identity.CastId);
                Assert.Equal(new Vector3(3f, 0f, 0f), arena.FiredFootprints[fired].Footprint.Origin);
                Assert.Equal(SpellState.Executing, first.State);
                Assert.Same(first, Assert.Single(Fly(sut, objects)));
                break;
            }

            case "a queued cast that never fired":
            {
                ProjectileAbilityScript first = Round(caster, ability);
                arena.ViewRound += 2;
                sut.Update(s_tick, objects);
                AbilityTemplate slow = AbilityTestData.Projectile(219, reach: 10f, speed: 20f).Dealing(10);
                slow.CastTime = 500u;
                GameAbility queued = AbilityTestData.Game(slow);
                Assert.True(sut.QueueAbility(caster, s_north, queued));

                // Moved: the cast is interrupted, as Complete_one_cast_and_interrupt_another_in_the_same_update does.
                caster.Position = new Vector3(1f, 0f, 0f);
                sut.Update(s_tick, objects);
                Assert.False(queued.Casting);

                Assert.True(sut.RunInstant(caster, s_north, ability));
                objects.Clear();
                sut.Update(s_tick, objects);
                Assert.Same(first, Assert.Single(objects));
                break;
            }

            case "after its flight threw":
            {
                Assert.True(sut.RunInstant(caster, s_north, ability));
                int steps = 0;
                arena.Navigator.RaycastWalkable(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(call =>
                    ++steps == 2 ? throw new InvalidOperationException("The navmesh failed.") : call.ArgAt<Vector3>(1));

                ProjectileAbilityScript first = Assert.Single(Fly(sut, objects));
                Assert.Null(sut.GetAbility(first.Guid));
                Assert.Same(caster, Assert.Single(arena.Interrupted).Caster);
                arena.ViewRound += 2;

                Assert.NotSame(first, Round(caster, ability));
                break;
            }

            case "past the pool's capacity":
            {
                HashSet<ProjectileAbilityScript> Batch()
                {
                    for (int i = 0; i <= InstanceAbilityCastSystem.PooledScripts; i++)
                    {
                        ability.CooldownTimer = 0f;
                        Assert.True(sut.RunInstant(caster, s_north, ability));
                    }

                    HashSet<ProjectileAbilityScript> flown = Fly(sut, objects);
                    Assert.Equal(InstanceAbilityCastSystem.PooledScripts + 1, flown.Count);
                    return flown;
                }

                HashSet<ProjectileAbilityScript> first = Batch();
                arena.ViewRound += 2;
                HashSet<ProjectileAbilityScript> second = Batch();

                Assert.Equal(InstanceAbilityCastSystem.PooledScripts, second.Count(first.Contains));
                break;
            }

            case "a piercing lane that ends first":
            {
                // A piercing fan of three (Twin Arrows on Piercing Arrow, here at most two hits a lane). The centre
                // lane ends on its second hit, 8.5 m out, its first 2 m out, both slim enough that the side lanes pass
                // them. Each side lane strikes a wide body 9 m out at -10 or +10 degrees, flies on through it for its
                // own count of one, and is still inside it when the centre lane ends and goes back: a set emptied then
                // would let it strike that body again. The creature 6 m north stands aside, out of every lane.
                creature.Position.Returns(new Vector3(-20f, 0f, 0f));
                ICreature near = arena.Creature(0f, 2f, body: 0.2f, health: 1000);
                ICreature far = arena.Creature(0f, 8.5f, body: 0.2f, health: 1000);
                float side = 9f * MathF.Sin(10f * MathF.PI / 180f);
                float ahead = 9f * MathF.Cos(10f * MathF.PI / 180f);
                ICreature left = arena.Creature(-side, ahead, body: 1.2f, health: 1000);
                ICreature right = arena.Creature(side, ahead, body: 1.2f, health: 1000);
                ICreature east = arena.Creature(6f, 0f, health: 1000);
                AbilityTemplate piercing =
                    AbilityTestData.Projectile(221, reach: 10f, speed: 20f, pierce: true).Dealing(10);
                piercing.MaxTargets = 2;
                Assert.True(sut.RunInstant(caster, s_north,
                    AbilityTestData.Shaped(piercing, (TalentModifierKind.AddedProjectiles, 2, null))));

                // 27 updates: the centre lane has ended and left, the side lanes are still inside their bodies.
                HashSet<ProjectileAbilityScript> fan = [];
                for (int i = 0; i < 27; i++)
                {
                    objects.Clear();
                    sut.Update(s_tick, objects);
                    foreach (IWorldObject handed in objects)
                    {
                        var lane = (ProjectileAbilityScript)handed;
                        lane.ConsumeDirtyFields();
                        fan.Add(lane);
                    }
                }

                Assert.Equal(3, fan.Count);
                ProjectileAbilityScript centre = Assert.Single(fan, lane => lane.State is SpellState.Finished);
                Assert.Null(sut.GetAbility(centre.Guid));
                arena.ViewRound += 2;

                Assert.True(sut.RunInstant(caster, new AbilityAim(new Vector3(1f, 0f, 0f), new Vector3(10f, 0f, 0f)),
                    ability));
                HashSet<ProjectileAbilityScript> rest = Fly(sut, objects);

                Assert.Equal(3, rest.Count);
                Assert.Contains(centre, rest);
                List<IUnit> damaged = arena.Damaged();
                Assert.Equal(5, damaged.Count);
                Assert.All(new[] { near, far, left, right, east }, unit => Assert.Single(damaged, unit));
                // Each side lane's one hit was not its last, the centre lane's second was.
                Assert.Equal([ProjectileImpactReason.Pierced, ProjectileImpactReason.Pierced],
                    arena.Impacts.Where(i => i.Target == left || i.Target == right).Select(i => i.Reason));
                Assert.Equal([ProjectileImpactReason.Pierced, ProjectileImpactReason.Hit],
                    arena.Impacts.Where(i => i.Target == near || i.Target == far).Select(i => i.Reason));
                break;
            }

            case "keeps nothing alive":
            {
                (WeakReference castBy, WeakReference metadata, ProjectileAbilityScript pooled) = CastAway();

                arena.Fired.Clear();
                arena.FiredFootprints.Clear();
                arena.Impacts.Clear();
                arena.Started.Clear();
                arena.Finished.Clear();
                arena.FinishedIds.Clear();
                arena.AurasApplied.Clear();
                arena.DamageDealt.Clear();
                arena.Shapings.Clear();
                arena.CombatService.ClearReceivedCalls();
                objects.Clear();

                // The substitutes keep the last call they routed, its arguments included, for a Returns that may follow
                // it: one more call, which names neither, lets the flight's last one go.
                _ = creature.Guid;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Assert.False(castBy.IsAlive);
                Assert.False(metadata.IsAlive);
                GC.KeepAlive(pooled);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(row), row, null);
        }

        // Cast by a caster and with an ability nothing else holds, flown to its end and retired.
        [MethodImpl(MethodImplOptions.NoInlining)]
        (WeakReference, WeakReference, ProjectileAbilityScript) CastAway()
        {
            CharacterEntity passing = TestCharacters.New(3);
            passing.Position = Vector3.zero;
            GameAbility held = AbilityTestData.Game(AbilityTestData.Projectile(219, reach: 10f, speed: 20f).Dealing(10));
            ProjectileAbilityScript flown = Round(passing, held);
            return (new WeakReference(passing), new WeakReference(held.Metadata), flown);
        }
    }

    /// <summary>
    /// Circles and cones are pooled per instance too (#1238): a script that fired is reused once two whole view rounds have
    /// passed, its own copy of the ability made again for the new cast, so the reused one fires the new cast's caster,
    /// origin, ability and shape, and the first fired the capstone shape (Wide Arc and Long Reach, or a wider circle) it was
    /// queued with. A hit the dodge wait still holds when its script is reused lands with its own cast's ability, not the
    /// next cast's, nor a cleared one. A script whose <c>Prepare</c> threw is never reused. The fired broadcast names the
    /// script's own copy, so the same copy fired twice is the same script.
    /// </summary>
    [Theory]
    [InlineData("a cone")]
    [InlineData("a circle")]
    [InlineData("a held hit")]
    [InlineData("after its Prepare threw")]
    public void Reuse_a_circle_or_cone_with_nothing_of_its_last_cast(string row)
    {
        var arena = new TestArena();
        var ticks = new WorldTicks(1_000);
        IServiceProvider services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(WorldTicks)).Returns(ticks);
        _scripts.GetAbilityScript(nameof(ConeAbilityScript)).Returns(typeof(ConeAbilityScript));
        _scripts.GetAbilityScript(nameof(CircleAbilityScript)).Returns(typeof(CircleAbilityScript));
        var sut = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, services, _scripts, arena);
        List<IWorldObject> objects = [];

        void Update()
        {
            objects.Clear();
            sut.Update(s_tick, objects);
            ticks.Advance();
        }

        void RunToFire(GameAbility queued)
        {
            for (int i = 0; i < 30 && queued.Casting; i++)
                Update();
            Assert.False(queued.Casting);
        }

        switch (row)
        {
            case "a cone":
            case "a circle":
            {
                bool cone = row == "a cone";
                AbilityTemplate first = cone ? AbilityTestData.Cone(203).Dealing(10) : AbilityTestData.Circle(201).Dealing(10);
                first.CastTime = 100u;
                GameAbility shaped = cone
                    ? AbilityTestData.Shaped(first, (TalentModifierKind.ConeArcPct, 50, null),
                        (TalentModifierKind.ConeReachPct, 50, null))
                    : AbilityTestData.Shaped(first, (TalentModifierKind.AreaPct, 40, null));
                GameAbility plain = AbilityTestData.Game(cone
                    ? AbilityTestData.Cone(212).Dealing(10)
                    : AbilityTestData.Circle(211).Dealing(10));
                CharacterEntity one = arena.Player(1, 0f, 0f);
                CharacterEntity two = arena.Player(2, 10f, 0f);
                ICreature near = arena.Creature(0f, 2f);
                ICreature far = arena.Creature(10f, 2f);

                Assert.True(sut.QueueAbility(one, s_north, shaped));
                RunToFire(shaped);
                arena.ViewRound += 2;
                Assert.True(sut.RunInstant(two, s_north, plain));

                Assert.Equal(2, arena.FiredAbilities.Count);
                Assert.Same(arena.FiredAbilities[0], arena.FiredAbilities[1]);
                AbilityFootprint queued = arena.FiredFootprints[0].Footprint;
                AbilityFootprint instant = arena.FiredFootprints[1].Footprint;
                if (cone)
                {
                    Assert.Equal((135f, 4.5f), (queued.ArcDegrees, queued.Reach));
                    Assert.Equal((90f, 3f), (instant.ArcDegrees, instant.Reach));
                }
                else
                {
                    Assert.Equal(4.2f, queued.Radius, 0.001f);
                    Assert.Equal(3f, instant.Radius);
                }

                Assert.Equal(new Vector3(10f, 0f, 0f), instant.Origin);
                Assert.Equal([(one, near, first.Id), (two, far, plain.AbilityId)],
                    arena.AurasApplied.Select(a => (a.Caster, a.Target, a.Id)));
                Assert.Same(shaped.Metadata, arena.AurasApplied[0].Metadata);
                break;
            }

            case "a held hit":
            {
                // A creature's telegraphed circle holds its hit on a player for four ticks; the circle's script is free
                // again before it lands, and another circle, of another ability, takes it.
                arena.Holds = (sut.PendingHits, ticks, 4);
                ICreature caster = arena.Creature(0f, 0f);
                CharacterEntity player = arena.Player(1, 0f, 2f);
                CharacterEntity bystander = arena.Player(2, 20f, 0f);
                AbilityTemplate novaRow = AbilityTestData.Circle(315).Dealing(10);
                novaRow.CastTime = 100u;
                GameAbility nova = AbilityTestData.Game(novaRow);
                Assert.True(sut.QueueAbility(caster, s_north, nova));
                RunToFire(nova);
                Assert.Empty(arena.AurasApplied);

                arena.ViewRound += 2;
                ICreature other = arena.Creature(20f, 2f);
                Assert.True(sut.RunInstant(bystander, s_north,
                    AbilityTestData.Game(AbilityTestData.Circle(211).Dealing(10))));
                Assert.Same(arena.FiredAbilities[0], arena.FiredAbilities[1]);
                for (int i = 0; i < 5; i++)
                    Update();

                Assert.Equal([(bystander, (IUnit)other, new AbilityId(211)), (caster, player, novaRow.Id)],
                    arena.AurasApplied.Select(a => (a.Caster, a.Target, a.Id)));
                Assert.Same(nova.Metadata, arena.AurasApplied[1].Metadata);
                break;
            }

            case "after its Prepare threw":
            {
                CharacterEntity caster = arena.Player(1, 0f, 0f);
                ICreature creature = arena.Creature(0f, 2f);
                bool failing = true;
                creature.Invulnerable.Returns(_ => failing ? throw new InvalidOperationException("The creature failed.") : false);
                GameAbility cone = AbilityTestData.Game(AbilityTestData.Cone(203).Dealing(10));

                Assert.True(sut.RunInstant(caster, s_north, cone));
                Assert.Same(caster, Assert.Single(arena.Interrupted).Caster);
                failing = false;
                arena.ViewRound += 2;
                Assert.True(sut.RunInstant(caster, s_north, cone));

                Assert.NotSame(arena.FiredAbilities[0], arena.FiredAbilities[1]);
                Assert.Equal(creature, Assert.Single(arena.AurasApplied).Target);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(row), row, null);
        }
    }

    /// <summary>
    /// Forty updates, each handing out the running projectiles and taking their changes as the broadcast does, so every
    /// flight ends and leaves; answers every projectile handed out.
    /// </summary>
    private static HashSet<ProjectileAbilityScript> Fly(InstanceAbilityCastSystem sut, List<IWorldObject> objects)
    {
        HashSet<ProjectileAbilityScript> flown = [];
        for (int i = 0; i < 40; i++)
        {
            objects.Clear();
            sut.Update(s_tick, objects);
            foreach (IWorldObject handed in objects)
            {
                var projectile = (ProjectileAbilityScript)handed;
                projectile.ConsumeDirtyFields();
                flown.Add(projectile);
            }
        }

        objects.Clear();
        return flown;
    }

    /// <summary>
    /// For an instance nobody is in (#164): a finished projectile goes at once, its final state unsent,
    /// and one still in flight stays.
    /// </summary>
    [Fact]
    public void Drop_a_finished_projectile_unsent_and_keep_one_in_flight()
    {
        var arena = new TestArena();
        _scripts.GetAbilityScript(nameof(ProjectileAbilityScript)).Returns(typeof(ProjectileAbilityScript));
        var sut = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, Substitute.For<IServiceProvider>(), _scripts, arena);
        arena.Creature(0f, 1f);
        var aim = new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 5f));
        Assert.True(sut.RunInstant(arena.Player(1, 0f, 0f), aim,
            AbilityTestData.Game(AbilityTestData.Projectile(1, reach: 5f, speed: 20f))));
        sut.Update(s_tick, []);   // the point-blank one hits and finishes
        Assert.True(sut.RunInstant(arena.Player(2, 10f, 0f), aim,
            AbilityTestData.Game(AbilityTestData.Projectile(2, reach: 20f, speed: 20f))));
        List<IWorldObject> before = [];
        sut.Update(s_tick, before);
        AbilityScript finished = Assert.Single(before.Cast<AbilityScript>(), s => s.State is SpellState.Finished);
        AbilityScript flying = Assert.Single(before.Cast<AbilityScript>(), s => s.State is not SpellState.Finished);
        Assert.True(finished.HasUnsentChanges);

        sut.DropFinished();

        Assert.Null(sut.GetAbility(finished.Guid));
        Assert.Same(flying, sut.GetAbility(flying.Guid));
    }
}
