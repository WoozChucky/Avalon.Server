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
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abilities;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// The cast system contains what its scripts throw (#530) and drops a leaving caster's scripts
/// (#541). RecordingAbilityScript's statics are shared, so this runs in that collection. Ability ids
/// and caster ids are in the 541_0xx range.
/// </summary>
[Collection(nameof(RecordingAbilityScript))]
public class InstanceAbilityCastSystemContainmentShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly AbilityAim s_aim = new(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 10f));

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
                     typeof(StateThrowingAbilityScript), typeof(GuidlessAbilityScript),
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
        ICharacter character = Substitute.For<ICharacter>();
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

        _sut.RunInstant(caster, s_aim, ability);

        AssertLoggedError(541_011, caster);
        Assert.Single(_arena.InterruptsOf(caster), ability);

        // Nothing is left behind: the next cast fires.
        Assert.True(_sut.RunInstant(caster, s_aim, Ability(541_012, nameof(RecordingAbilityScript))));
        Assert.Single(RecordingAbilityScript.Prepared);
    }

    [Fact]
    public void Contain_a_Prepare_that_throws_on_the_queued_path_and_still_complete_another_cast()
    {
        ICharacter failing = Caster(541_002);
        ICharacter other = Caster(541_003);
        GameAbility broken = Ability(541_013, nameof(PrepareThrowingAbilityScript), castTime: 0.01f);
        GameAbility fine = Ability(541_014, nameof(RecordingAbilityScript), castTime: 0.01f);
        Assert.True(_sut.QueueAbility(failing, s_aim, broken));
        Assert.True(_sut.QueueAbility(other, s_aim, fine));

        _sut.Update(s_tick, []);

        AssertLoggedError(541_013, failing);
        Assert.Single(_arena.InterruptsOf(failing), broken);
        Assert.False(broken.Casting);
        (IUnit who, _, _) = Assert.Single(RecordingAbilityScript.Prepared);
        Assert.Same(other, who);
        Assert.Empty(_arena.InterruptsOf(other));
    }

    // ── #530: a throwing Update is contained, and the other scripts in that tick still run ──

    [Fact]
    public void Drop_a_script_whose_Update_throws_interrupt_its_caster_and_keep_ticking_the_others()
    {
        ICharacter failing = Caster(541_004);
        ICharacter other = _arena.Player(541_005, 0f, 0f);
        _arena.Creature(0f, 5f);
        GameAbility broken = Ability(541_015, nameof(UpdateThrowingAbilityScript));
        Assert.True(_sut.RunInstant(failing, s_aim, broken));   // first in the list, so it throws ahead of the other
        Assert.True(_sut.RunInstant(other, s_aim, Projectile(541_016)));

        List<IWorldObject> objects = [];
        for (int i = 0; i < UpdateThrowingAbilityScript.ThrowOnUpdate - 1; i++)
        {
            objects.Clear();
            _sut.Update(s_tick, objects);
        }

        Assert.Equal(2, objects.Count);
        UpdateThrowingAbilityScript thrower = Assert.Single(objects.OfType<UpdateThrowingAbilityScript>());
        ProjectileAbilityScript projectile = Assert.Single(objects.OfType<ProjectileAbilityScript>());
        Vector3 before = projectile.Position;

        objects.Clear();
        _sut.Update(s_tick, objects);   // the thrower's tenth Update throws

        Assert.Equal(UpdateThrowingAbilityScript.ThrowOnUpdate, thrower.Updates);
        AssertLoggedError(541_015, failing);
        Assert.Single(_arena.InterruptsOf(failing), broken);
        Assert.Same(projectile, Assert.Single(objects));   // the thrower is no longer a world object
        Assert.NotEqual(before, projectile.Position);      // the other one was ticked in the same update
        Assert.Null(_sut.GetAbility(thrower.Guid));

        // It is never ticked again, and the projectile still lands.
        for (int i = 0; i < 90; i++)
        {
            _sut.Update(s_tick, []);
        }

        Assert.Equal(UpdateThrowingAbilityScript.ThrowOnUpdate, thrower.Updates);
        Assert.Single(_arena.Damaged());
        Assert.Single(_arena.InterruptsOf(failing));
    }

    /// <summary>
    /// A script that leaves Prepare running without a guid is contained like one that throws: logged, dropped and
    /// its caster sent the interrupt, while the other scripts tick, on that update and the next. Read unchecked, its
    /// missing guid threw out of every update of the instance.
    /// </summary>
    [Fact]
    public void Drop_a_script_running_without_a_guid_and_keep_ticking_the_others()
    {
        ICharacter failing = Caster(541_070);
        ICharacter flyer = _arena.Player(541_071, 0f, 0f);
        Assert.True(_sut.RunInstant(failing, s_aim, Ability(541_072, nameof(GuidlessAbilityScript))));
        Assert.True(_sut.RunInstant(flyer, s_aim, Projectile(541_073)));

        List<IWorldObject> objects = [];
        _sut.Update(s_tick, objects);

        Assert.Single(objects);   // the projectile still ticked
        AssertLoggedError(541_072, failing);
        Assert.Single(_arena.InterruptsOf(failing));

        objects.Clear();
        _sut.Update(s_tick, objects);   // the script is gone: nothing throws, nothing is sent again

        Assert.Single(objects);
        Assert.Single(_arena.InterruptsOf(failing));
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
        _arena.InterruptThrowsFor = dead;
        ICharacter other = Caster(541_009);
        ICharacter flyer = _arena.Player(541_010, 0f, 0f);
        GameAbility dropped = Ability(541_019, nameof(RecordingAbilityScript), castTime: 0.01f);
        GameAbility fine = Ability(541_020, nameof(RecordingAbilityScript), castTime: 0.01f);
        Assert.True(_sut.QueueAbility(dead, s_aim, dropped));
        Assert.True(_sut.QueueAbility(other, s_aim, fine));
        Assert.True(_sut.RunInstant(flyer, s_aim, Projectile(541_021)));
        dead.IsDead.Returns(true);

        List<IWorldObject> objects = [];
        _sut.Update(s_tick, objects);

        Assert.Single(_arena.InterruptsOf(dead), dropped);
        Assert.False(dropped.Casting);
        (IUnit who, _, _) = Assert.Single(RecordingAbilityScript.Prepared);
        Assert.Same(other, who);
        Assert.Single(objects);   // the projectile still ticked

        _sut.Update(s_tick, []);   // the cast is gone: nothing throws, nothing is sent again

        Assert.Single(_arena.InterruptsOf(dead));
        Assert.Contains(_logs.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
    }

    // ── #541: a leaving caster takes its scripts with it ──

    [Fact]
    public void Drop_only_the_leaving_casters_scripts()
    {
        ICharacter leaving = _arena.Player(541_006, 0f, 0f);
        ICharacter staying = _arena.Player(541_007, 0f, 0f);
        _arena.Creature(0f, 5f);
        Assert.True(_sut.RunInstant(leaving, s_aim, Projectile(541_017)));
        Assert.True(_sut.RunInstant(staying, s_aim, Projectile(541_018)));
        List<IWorldObject> objects = [];
        _sut.Update(s_tick, objects);
        Assert.Equal(2, objects.Count);

        _sut.CancelScriptsOf(leaving);

        for (int i = 0; i < 90; i++)
        {
            objects.Clear();
            _sut.Update(s_tick, objects);
            Assert.True(objects.Count <= 1);
        }

        // Only the staying caster's projectile hit.
        Assert.Single(_arena.Damaged());
        Assert.Same(staying, _arena.CombatService.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(Avalon.World.Public.Combat.ICombatService.ApplyDamage))
            .GetArguments()[0]);
    }

    // ── #883: the index GetAbility answers from follows the active scripts ──

    /// <summary>
    /// With more active scripts than it walks, GetAbility answers from an index by guid, filled again after the
    /// active scripts change (#883). Each way a script leaves is followed here by a lookup made after an earlier
    /// lookup filled the index: a removal that did not reach the index would answer with a script that left.
    /// </summary>
    [Fact]
    public void Find_exactly_the_scripts_still_running_however_they_came_and_went()
    {
        // In flight for the whole test, and enough of them that every lookup below is answered from the index.
        ICharacter crowd = _arena.Player(541_029, 0f, 0f);
        for (uint i = 0; i <= InstanceAbilityCastSystem.WalkedUpTo; i++)
        {
            Assert.True(_sut.RunInstant(crowd, s_aim, Projectile(541_040 + i)));
        }

        ICharacter leaving = _arena.Player(541_030, 0f, 0f);
        ICharacter staying = _arena.Player(541_031, 0f, 0f);
        Assert.True(_sut.RunInstant(leaving, s_aim, Projectile(541_032)));
        List<IWorldObject> objects = [];
        _sut.Update(s_tick, objects);
        IWorldObject flying = objects[^1];
        Assert.Same(flying, _sut.GetAbility(flying.Guid));

        // Two join: one that throws on a later update, and one that ends at its reach on its first.
        Assert.True(_sut.RunInstant(staying, s_aim, Ability(541_033, nameof(UpdateThrowingAbilityScript))));
        Assert.True(_sut.RunInstant(staying, s_aim,
            AbilityTestData.Game(AbilityTestData.Projectile(541_034, reach: 0.05f, speed: 20f))));
        objects.Clear();
        _sut.Update(s_tick, objects);
        IWorldObject thrower = Assert.Single(objects, o => o is UpdateThrowingAbilityScript);
        IWorldObject ended = objects[^1];
        Assert.Same(thrower, _sut.GetAbility(thrower.Guid));
        Assert.Same(ended, _sut.GetAbility(ended.Guid));

        // One leaves with its caster.
        _sut.CancelScriptsOf(leaving);
        Assert.Null(_sut.GetAbility(flying.Guid));
        Assert.Same(ended, _sut.GetAbility(ended.Guid));

        // One is dropped finished, its final state unsent.
        _sut.DropFinished();
        Assert.Null(_sut.GetAbility(ended.Guid));
        Assert.Same(thrower, _sut.GetAbility(thrower.Guid));

        // One is dropped for throwing.
        for (int i = 0; i < UpdateThrowingAbilityScript.ThrowOnUpdate; i++)
        {
            _sut.Update(s_tick, []);
        }

        Assert.Null(_sut.GetAbility(thrower.Guid));
    }

    /// <summary>
    /// A script's State is its own code, read when it has prepared, before each Update and by both sweeps for
    /// finished scripts. One that throws there is contained like a throwing Update: logged once, dropped, its caster
    /// sent the interrupt, and every other script still there once, ticking and found by its guid. Uncontained, the
    /// sweep threw out of every update of the instance, each time leaving the list with a script twice and an index
    /// that named one the list had dropped (#883).
    /// </summary>
    [Theory]
    [InlineData("when it has prepared")]
    [InlineData("in an update")]
    [InlineData("in the sweep of an empty instance")]
    public void Drop_a_script_whose_state_cannot_be_read_and_keep_the_others(string read)
    {
        ICharacter failing = Caster(541_053);
        ICharacter caster = _arena.Player(541_050, 0f, 0f);
        Assert.True(_sut.RunInstant(caster, s_aim,
            AbilityTestData.Game(AbilityTestData.Projectile(541_051, reach: 0.05f, speed: 20f))));
        for (uint i = 0; i < InstanceAbilityCastSystem.WalkedUpTo; i++)
        {
            Assert.True(_sut.RunInstant(caster, s_aim, Projectile(541_060 + i)));
        }

        StateThrowingAbilityScript.ArmedWhenBuilt = read == "when it has prepared";
        try
        {
            Assert.True(_sut.RunInstant(failing, s_aim, Ability(541_052, nameof(StateThrowingAbilityScript))));
        }
        finally
        {
            StateThrowingAbilityScript.ArmedWhenBuilt = false;
        }

        // The first update ends the short projectile, which stays for its final state, and fills the index.
        List<IWorldObject> objects = [];
        _sut.Update(s_tick, objects);
        IWorldObject ended = objects[0];
        Assert.Same(ended, _sut.GetAbility(ended.Guid));
        if (objects[^1] is StateThrowingAbilityScript unreadable)
        {
            unreadable.Armed = true;
        }

        if (read == "in the sweep of an empty instance")
        {
            _sut.DropFinished();
            Assert.Null(_sut.GetAbility(ended.Guid));
        }

        for (int i = 0; i < 2; i++)
        {
            objects.Clear();
            _sut.Update(s_tick, objects);
        }

        Assert.Single(_logs.Entries, e => e.Level == LogLevel.Error);
        AssertLoggedError(541_052, failing);
        Assert.Single(_arena.InterruptsOf(failing));
        Assert.DoesNotContain(objects, o => o is StateThrowingAbilityScript);
        Assert.Equal(objects.Count, objects.Distinct().Count());
        Assert.Equal(InstanceAbilityCastSystem.WalkedUpTo + (read == "in the sweep of an empty instance" ? 0 : 1),
            objects.Count);
        Assert.All(objects, o => Assert.Same(o, _sut.GetAbility(o.Guid)));
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
