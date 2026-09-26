using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Scripts;
using Avalon.World.Abilities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>RecordingAbilityScript's statics are shared, so the classes reading them never run in parallel.</summary>
[CollectionDefinition(nameof(RecordingAbilityScript), DisableParallelization = true)]
public sealed class RecordingAbilityScriptCollection;

[Collection(nameof(RecordingAbilityScript))]
public class InstanceAbilityCastSystemShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly AbilityAim Aim = new(new Vector3(0f, 0f, 1f), new Vector3(3f, 0f, 4f));

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

    private static GameAbility Ability(uint cost = 0, float castTime = 0f, string script = "Recording") => new()
    {
        AbilityId = new AbilityId(1),
        Metadata = new AbilityMetadata { Name = "x", ScriptName = script, Cost = cost, CastTime = castTime, Cooldown = 2f },
        CastTimeTimer = castTime,
        CooldownTimer = 0f,
    };

    private static ICharacter Caster(PowerType type = PowerType.Mana, uint power = 100)
    {
        var character = Substitute.For<ICharacter>();
        character.PowerType.Returns(type);
        character.CurrentPower.Returns(power);
        character.Position.Returns(Vector3.zero);
        return character;
    }

    // ── #521 item 1: Casting is set only once the queue took the cast ──

    [Fact]
    public void Leave_Casting_clear_when_the_queue_refuses_the_cast()
    {
        GameAbility ability = Ability(cost: 30, castTime: 1f);

        bool queued = _sut.QueueAbility(Caster(PowerType.None), Aim, ability);

        Assert.False(queued);
        Assert.False(ability.Casting);
    }

    [Fact]
    public void Set_Casting_and_pay_once_the_cast_is_queued()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30, castTime: 1f);

        Assert.True(_sut.QueueAbility(caster, Aim, ability));

        Assert.True(ability.Casting);
        caster.Received(1).CurrentPower = 70u;
    }

    /// <summary>A queued cast whose script cannot be found spends nothing and does not start casting.</summary>
    [Fact]
    public void Queue_nothing_and_spend_nothing_when_the_script_is_missing()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30, castTime: 1f, script: "Nope");

        Assert.False(_sut.QueueAbility(caster, Aim, ability));

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

        bool accepted = castTime > 0 ? _sut.QueueAbility(caster, Aim, ability) : _sut.RunInstant(caster, Aim, ability);

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
        Assert.True(_sut.QueueAbility(caster, Aim, ability));
        _scripts.GetAbilityScript("Recording").Returns((Type?)null);

        _sut.Update(Tick, []);

        Assert.Equal(Aim, Assert.Single(RecordingAbilityScript.Prepared).Aim);
        caster.Received(1).CurrentPower = 70u;
        Assert.Equal(2f, ability.CooldownTimer);
    }

    // ── #521 item 2: one power rule on both paths ──

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Refuse_a_costed_cast_by_a_caster_without_a_pool_on_both_paths(float castTime)
    {
        ICharacter caster = Caster(PowerType.None);
        GameAbility ability = Ability(cost: 30, castTime: castTime);

        bool accepted = castTime > 0 ? _sut.QueueAbility(caster, Aim, ability) : _sut.RunInstant(caster, Aim, ability);

        Assert.False(accepted);
        caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
        Assert.Equal(0f, ability.CooldownTimer);
    }

    /// <summary>Fury is spendable on both paths (#526), and nothing generates it yet.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Take_and_charge_a_Fury_cast_the_pool_can_pay_on_both_paths(float castTime)
    {
        ICharacter caster = Caster(PowerType.Fury, power: 30);
        GameAbility ability = Ability(cost: 20, castTime: castTime);

        bool accepted = castTime > 0 ? _sut.QueueAbility(caster, Aim, ability) : _sut.RunInstant(caster, Aim, ability);

        Assert.True(accepted);
        caster.Received(1).CurrentPower = 10u;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void Refuse_a_Fury_cast_the_pool_cannot_pay_on_both_paths(float castTime)
    {
        ICharacter caster = Caster(PowerType.Fury, power: 19);
        GameAbility ability = Ability(cost: 20, castTime: castTime);

        bool accepted = castTime > 0 ? _sut.QueueAbility(caster, Aim, ability) : _sut.RunInstant(caster, Aim, ability);

        Assert.False(accepted);
        Assert.False(ability.Casting);
        caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
        Assert.Empty(RecordingAbilityScript.Prepared);
    }

    [Fact]
    public void Pay_start_the_cooldown_and_fire_an_instant_cast_with_its_aim()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30);

        Assert.True(_sut.RunInstant(caster, Aim, ability));

        caster.Received(1).CurrentPower = 70u;
        Assert.Equal(2f, ability.CooldownTimer);
        caster.Received(1).SendFinishCastAnimation(ability);
        (IUnit who, AbilityAim aim, IAbilityArena arena) = Assert.Single(RecordingAbilityScript.Prepared);
        Assert.Same(caster, who);
        Assert.Equal(Aim, aim);
        Assert.Same(_arena, arena);
    }

    [Fact]
    public void Spend_nothing_when_the_script_is_missing()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(cost: 30, script: "Nope");

        Assert.False(_sut.RunInstant(caster, Aim, ability));

        caster.DidNotReceive().CurrentPower = Arg.Any<uint?>();
        Assert.Equal(0f, ability.CooldownTimer);
    }

    // ── the aim captured at cast start is the one the script gets ──

    [Fact]
    public void Fire_a_queued_cast_with_the_aim_it_was_queued_with()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(castTime: 0.01f);
        _sut.QueueAbility(caster, Aim, ability);

        _sut.Update(Tick, []);

        Assert.Equal(Aim, Assert.Single(RecordingAbilityScript.Prepared).Aim);
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
        _sut.QueueAbility(still, Aim, finishing);
        _sut.QueueAbility(mover, Aim, interrupted);
        mover.Position.Returns(new Vector3(1f, 0f, 0f));

        _sut.Update(Tick, []);
        _sut.Update(Tick, []);

        Assert.Single(RecordingAbilityScript.Prepared);
        Assert.False(finishing.Casting);
        Assert.False(interrupted.Casting);
        mover.Received(1).SendInterruptedCastAnimation(interrupted);
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
        Assert.True(_sut.QueueAbility(leaving, Aim, cancelled));
        Assert.True(_sut.QueueAbility(staying, Aim, kept));
        _sut.Update(Tick, []);

        _sut.CancelCasts(leaving);

        Assert.False(cancelled.Casting);
        Assert.Equal(0.6f, cancelled.CastTimeTimer);
        leaving.Received(1).SendInterruptedCastAnimation(cancelled);
        leaving.Received(1).CurrentPower = 70u;
        Assert.True(kept.Casting);
        staying.DidNotReceive().SendInterruptedCastAnimation(Arg.Any<IAbility>());
        Assert.Empty(RecordingAbilityScript.Prepared);

        for (int i = 0; i < 40; i++)
        {
            _sut.Update(Tick, []);
        }

        (IUnit who, _, _) = Assert.Single(RecordingAbilityScript.Prepared);
        Assert.Same(staying, who);
        leaving.DidNotReceive().SendFinishCastAnimation(Arg.Any<IAbility>());
        Assert.Equal(0f, cancelled.CooldownTimer);
    }

    // ── a dead caster's cast is dropped at completion ──

    [Fact]
    public void Drop_a_cast_whose_caster_died_before_it_completed_and_take_the_next_one()
    {
        ICharacter caster = Caster();
        GameAbility ability = Ability(castTime: 0.01f);
        _sut.QueueAbility(caster, Aim, ability);
        caster.IsDead.Returns(true);

        _sut.Update(Tick, []);

        Assert.Empty(RecordingAbilityScript.Prepared);
        Assert.False(ability.Casting);
        caster.DidNotReceive().SendFinishCastAnimation(Arg.Any<IAbility>());

        // Nothing is left behind that would refuse the next cast: the queue takes it again.
        caster.IsDead.Returns(false);
        Assert.True(_sut.QueueAbility(caster, Aim, ability));
        _sut.Update(Tick, []);
        Assert.Single(RecordingAbilityScript.Prepared);
    }

    /// <summary>A script that finished in Prepare (a circle, a cone) never enters the active list.</summary>
    [Fact]
    public void Keep_no_finished_script_in_the_active_list()
    {
        _sut.RunInstant(Caster(), Aim, Ability());

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

        bool accepted = castTime > 0 ? sut.QueueAbility(caster, Aim, ability) : sut.RunInstant(caster, Aim, ability);
        for (int i = 0; castTime > 0 && i < 61; i++)
        {
            sut.Update(Tick, []);
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
            sut.Update(Tick, objects);
        }

        AbilityScript projectile = Assert.IsAssignableFrom<AbilityScript>(Assert.Single(objects));
        Assert.Equal(SpellState.Finished, projectile.State);
        Assert.Single(arena.Damaged());

        // Its final state is still owed to the clients: it stays.
        objects.Clear();
        sut.Update(Tick, objects);
        Assert.Same(projectile, Assert.Single(objects));

        Assert.NotEqual(GameEntityFields.None, projectile.ConsumeDirtyFields());
        objects.Clear();
        sut.Update(Tick, objects);

        Assert.Empty(objects);
        Assert.Null(sut.GetAbility(projectile.Guid));
        Assert.Single(arena.Damaged());
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
        sut.Update(Tick, []);   // the point-blank one hits and finishes
        Assert.True(sut.RunInstant(arena.Player(2, 10f, 0f), aim,
            AbilityTestData.Game(AbilityTestData.Projectile(2, reach: 20f, speed: 20f))));
        List<IWorldObject> before = [];
        sut.Update(Tick, before);
        AbilityScript finished = Assert.Single(before.Cast<AbilityScript>(), s => s.State is SpellState.Finished);
        AbilityScript flying = Assert.Single(before.Cast<AbilityScript>(), s => s.State is not SpellState.Finished);
        Assert.True(finished.HasUnsentChanges);

        sut.DropFinished();

        Assert.Null(sut.GetAbility(finished.Guid));
        Assert.Same(flying, sut.GetAbility(flying.Guid));
    }
}
