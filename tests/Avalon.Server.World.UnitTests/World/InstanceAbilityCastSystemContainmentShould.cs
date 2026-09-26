using Avalon.Common;
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
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// The cast system contains what its scripts throw (#530) and drops a leaving caster's scripts
/// (#541). RecordingAbilityScript's statics are shared, so this runs in that collection. Ability ids
/// and caster ids are in the 541_0xx range.
/// </summary>
[Collection(nameof(RecordingAbilityScript))]
public class InstanceAbilityCastSystemContainmentShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly AbilityAim Aim = new(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 10f));

    private readonly IScriptManager _scripts = Substitute.For<IScriptManager>();
    private readonly RecordingLoggerFactory _logs = new();
    private readonly TestArena _arena = new();
    private readonly InstanceAbilityCastSystem _sut;

    public InstanceAbilityCastSystemContainmentShould()
    {
        RecordingAbilityScript.Prepared.Clear();
        RecordingAbilityScript.LastBuilt = null;
        foreach (Type script in new[]
                 {
                     typeof(RecordingAbilityScript), typeof(PrepareThrowingAbilityScript),
                     typeof(UpdateThrowingAbilityScript), typeof(ProjectileAbilityScript),
                 })
        {
            _scripts.GetAbilityScript(script.Name).Returns(script);
        }

        _sut = new InstanceAbilityCastSystem(_logs, Substitute.For<IServiceProvider>(), _scripts, _arena);
    }

    private static GameAbility Ability(uint id, string script, float castTime = 0f) => new()
    {
        AbilityId = new AbilityId(id),
        Metadata = new AbilityMetadata { Name = "x", ScriptName = script, CastTime = castTime, Cooldown = 2f },
        CastTimeTimer = castTime,
        CooldownTimer = 0f,
    };

    private static ICharacter Caster(uint id)
    {
        var character = Substitute.For<ICharacter>();
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, id));
        character.PowerType.Returns(PowerType.Mana);
        character.CurrentPower.Returns(100u);
        character.Position.Returns(Vector3.zero);
        return character;
    }

    private static GameAbility Projectile(uint id, float speed = 5f) =>
        AbilityTestData.Game(AbilityTestData.Projectile(id, reach: 20f, speed: speed));

    private void AssertLoggedError(uint abilityId, ICharacter caster)
    {
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error
            && e.Exception is InvalidOperationException
            && e.Message.Contains(abilityId.ToString(), StringComparison.Ordinal)
            && e.Message.Contains(caster.Guid.ToString(), StringComparison.Ordinal));
    }

    // ── #530: a throwing Prepare is contained on both paths ──

    [Fact]
    public void Contain_a_Prepare_that_throws_on_the_instant_path_and_interrupt_the_caster()
    {
        ICharacter caster = Caster(541_001);
        GameAbility ability = Ability(541_011, nameof(PrepareThrowingAbilityScript));

        _sut.RunInstant(caster, Aim, ability);

        AssertLoggedError(541_011, caster);
        caster.Received(1).SendInterruptedCastAnimation(ability);

        // Nothing is left behind: the next cast fires.
        Assert.True(_sut.RunInstant(caster, Aim, Ability(541_012, nameof(RecordingAbilityScript))));
        Assert.Single(RecordingAbilityScript.Prepared);
    }

    [Fact]
    public void Contain_a_Prepare_that_throws_on_the_queued_path_and_still_complete_another_cast()
    {
        ICharacter failing = Caster(541_002);
        ICharacter other = Caster(541_003);
        GameAbility broken = Ability(541_013, nameof(PrepareThrowingAbilityScript), castTime: 0.01f);
        GameAbility fine = Ability(541_014, nameof(RecordingAbilityScript), castTime: 0.01f);
        Assert.True(_sut.QueueAbility(failing, Aim, broken));
        Assert.True(_sut.QueueAbility(other, Aim, fine));

        _sut.Update(Tick, []);

        AssertLoggedError(541_013, failing);
        failing.Received(1).SendInterruptedCastAnimation(broken);
        Assert.False(broken.Casting);
        (IUnit who, _, _) = Assert.Single(RecordingAbilityScript.Prepared);
        Assert.Same(other, who);
        other.DidNotReceive().SendInterruptedCastAnimation(Arg.Any<IAbility>());
    }

    // ── #530: a throwing Update is contained, and the other scripts in that tick still run ──

    [Fact]
    public void Drop_a_script_whose_Update_throws_interrupt_its_caster_and_keep_ticking_the_others()
    {
        ICharacter failing = Caster(541_004);
        ICharacter other = _arena.Player(541_005, 0f, 0f);
        _arena.Creature(0f, 5f);
        GameAbility broken = Ability(541_015, nameof(UpdateThrowingAbilityScript));
        Assert.True(_sut.RunInstant(failing, Aim, broken));   // first in the list, so it throws ahead of the other
        Assert.True(_sut.RunInstant(other, Aim, Projectile(541_016)));

        List<IWorldObject> objects = [];
        for (int i = 0; i < UpdateThrowingAbilityScript.ThrowOnUpdate - 1; i++)
        {
            objects.Clear();
            _sut.Update(Tick, objects);
        }

        Assert.Equal(2, objects.Count);
        var thrower = Assert.Single(objects.OfType<UpdateThrowingAbilityScript>());
        var projectile = Assert.Single(objects.OfType<ProjectileAbilityScript>());
        Vector3 before = projectile.Position;

        objects.Clear();
        _sut.Update(Tick, objects);   // the thrower's tenth Update throws

        Assert.Equal(UpdateThrowingAbilityScript.ThrowOnUpdate, thrower.Updates);
        AssertLoggedError(541_015, failing);
        failing.Received(1).SendInterruptedCastAnimation(broken);
        Assert.Same(projectile, Assert.Single(objects));   // the thrower is no longer a world object
        Assert.NotEqual(before, projectile.Position);      // the other one was ticked in the same update
        Assert.Null(_sut.GetAbility(thrower.Guid));

        // It is never ticked again, and the projectile still lands.
        for (int i = 0; i < 90; i++)
        {
            _sut.Update(Tick, []);
        }

        Assert.Equal(UpdateThrowingAbilityScript.ThrowOnUpdate, thrower.Updates);
        Assert.Single(_arena.Damaged());
        failing.Received(1).SendInterruptedCastAnimation(Arg.Any<IAbility>());
    }

    // ── #530: an interrupt send that throws cannot leave a cast queued ──

    /// <summary>
    /// The dead-caster interrupt is sent once the cast is already out of the queue, and contained: a
    /// send that throws still dequeues the cast, the other cast and scripts still run, and the next
    /// update does not throw again.
    /// </summary>
    [Fact]
    public void Dequeue_a_dead_casters_cast_even_when_its_interrupt_send_throws()
    {
        ICharacter dead = Caster(541_008);
        dead.When(c => c.SendInterruptedCastAnimation(Arg.Any<IAbility>()))
            .Do(_ => throw new InvalidOperationException("The send failed."));
        ICharacter other = Caster(541_009);
        ICharacter flyer = _arena.Player(541_010, 0f, 0f);
        GameAbility dropped = Ability(541_019, nameof(RecordingAbilityScript), castTime: 0.01f);
        GameAbility fine = Ability(541_020, nameof(RecordingAbilityScript), castTime: 0.01f);
        Assert.True(_sut.QueueAbility(dead, Aim, dropped));
        Assert.True(_sut.QueueAbility(other, Aim, fine));
        Assert.True(_sut.RunInstant(flyer, Aim, Projectile(541_021)));
        dead.IsDead.Returns(true);

        List<IWorldObject> objects = [];
        _sut.Update(Tick, objects);

        dead.Received(1).SendInterruptedCastAnimation(dropped);
        Assert.False(dropped.Casting);
        (IUnit who, _, _) = Assert.Single(RecordingAbilityScript.Prepared);
        Assert.Same(other, who);
        Assert.Single(objects);   // the projectile still ticked

        _sut.Update(Tick, []);   // the cast is gone: nothing throws, nothing is sent again

        dead.Received(1).SendInterruptedCastAnimation(Arg.Any<IAbility>());
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    // ── #541: a leaving caster takes its scripts with it ──

    [Fact]
    public void Drop_only_the_leaving_casters_scripts()
    {
        ICharacter leaving = _arena.Player(541_006, 0f, 0f);
        ICharacter staying = _arena.Player(541_007, 0f, 0f);
        _arena.Creature(0f, 5f);
        Assert.True(_sut.RunInstant(leaving, Aim, Projectile(541_017)));
        Assert.True(_sut.RunInstant(staying, Aim, Projectile(541_018)));
        List<IWorldObject> objects = [];
        _sut.Update(Tick, objects);
        Assert.Equal(2, objects.Count);

        _sut.CancelScriptsOf(leaving);

        for (int i = 0; i < 90; i++)
        {
            objects.Clear();
            _sut.Update(Tick, objects);
            Assert.True(objects.Count <= 1);
        }

        // Only the staying caster's projectile hit.
        Assert.Single(_arena.Damaged());
        Assert.Same(staying, _arena.CombatService.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(Avalon.World.Public.Combat.ICombatService.ApplyDamage))
            .GetArguments()[0]);
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
