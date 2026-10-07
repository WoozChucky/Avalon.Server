using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.World;

/// <summary>
/// #648: the cast system broadcasts a cast-time cast's start with its id and the footprint it will land on, and
/// the cast fires with exactly that footprint, from where the caster stood at cast start, under the same id.
/// The real shape scripts, over a <see cref="TestArena" />.
/// </summary>
public class CastFootprintShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    private readonly TestArena _arena = new();
    private readonly InstanceAbilityCastSystem _sut;

    public CastFootprintShould()
    {
        IScriptManager scripts = Substitute.For<IScriptManager>();
        scripts.GetAbilityScript(nameof(CircleAbilityScript)).Returns(typeof(CircleAbilityScript));
        scripts.GetAbilityScript(nameof(ConeAbilityScript)).Returns(typeof(ConeAbilityScript));
        _sut = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, Substitute.For<IServiceProvider>(), scripts, _arena);
    }

    private static GameAbility Timed(Avalon.Domain.World.AbilityTemplate template, uint castTimeMs)
    {
        template.CastTime = castTimeMs;
        return AbilityTestData.Game(template);
    }

    /// <summary>A living creature at <paramref name="position" />: a creature is never interrupted by being moved (#163).</summary>
    private static ICreature Creature(Vector3 position)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 648_900u));
        creature.Position.Returns(position);
        creature.CurrentHealth.Returns(10u);
        return creature;
    }

    private void RunOut(float seconds)
    {
        for (int i = 0; i < (int)(seconds * 60f) + 2; i++)
        {
            _sut.Update(s_tick, []);
        }
    }

    [Fact]
    public void Broadcast_the_start_with_an_id_and_the_footprint_the_cast_fires_on()
    {
        ICreature caster = Creature(new Vector3(1f, 0f, 1f));
        GameAbility burst = Timed(AbilityTestData.AimedCircle(311, reach: 10f, radius: 3f), castTimeMs: 200);

        Assert.True(_sut.QueueAbility(caster, new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 21f)), burst));

        (_, _, uint castId, AbilityFootprint? started) = Assert.Single(_arena.Started);
        Assert.NotEqual(0u, castId);
        Assert.Equal(new Vector3(1f, 0f, 11f), started!.Value.Centre);   // clamped to the 10 m reach
        Assert.Equal(3f, started.Value.Radius);

        RunOut(0.2f);

        (AbilityFootprint fired, uint firedId) = Assert.Single(_arena.FiredFootprints);
        Assert.Equal(started.Value, fired);
        Assert.Equal(castId, firedId);
        Assert.Equal([castId], _arena.FinishedIds);
    }

    /// <summary>
    /// A creature pushed during its wind-up (a crowd's separation) still fires where its telegraph was drawn:
    /// the cast resolves from where it stood at cast start, not where it stands when it fires.
    /// </summary>
    [Fact]
    public void Fire_where_the_telegraph_was_drawn_after_the_caster_was_pushed()
    {
        ICreature caster = Creature(Vector3.zero);
        GameAbility nova = Timed(AbilityTestData.Circle(315, radius: 4f), castTimeMs: 200);
        _sut.QueueAbility(caster, new AbilityAim(new Vector3(0f, 0f, 1f), null), nova);

        caster.Position.Returns(new Vector3(0.4f, 0f, 0f));
        RunOut(0.2f);

        (AbilityFootprint fired, _) = Assert.Single(_arena.FiredFootprints);
        Assert.Equal(Vector3.zero, fired.Centre);
        Assert.Equal(_arena.Started.Single().Footprint, fired);
    }

    [Fact]
    public void Give_every_cast_its_own_id()
    {
        ICreature caster = Creature(Vector3.zero);
        GameAbility first = Timed(AbilityTestData.Circle(1), castTimeMs: 100);
        GameAbility second = Timed(AbilityTestData.Circle(2), castTimeMs: 100);

        _sut.QueueAbility(caster, new AbilityAim(new Vector3(0f, 0f, 1f), null), first);
        RunOut(0.1f);
        _sut.QueueAbility(caster, new AbilityAim(new Vector3(0f, 0f, 1f), null), second);

        Assert.Equal(2, _arena.Started.Select(s => s.CastId).Distinct().Count());
    }

    /// <summary>An interrupted cast names the id its start carried, so a client clears that telegraph and no other.</summary>
    [Fact]
    public void Name_the_casts_id_on_its_interrupt()
    {
        ICreature caster = Creature(Vector3.zero);
        GameAbility nova = Timed(AbilityTestData.Circle(315), castTimeMs: 1000);
        _sut.QueueAbility(caster, new AbilityAim(new Vector3(0f, 0f, 1f), null), nova);

        _sut.CancelCasts(caster);

        Assert.Equal([_arena.Started.Single().CastId], _arena.InterruptedIds);
        Assert.Empty(_arena.FiredFootprints);
    }

    /// <summary>An instant cast has no start and no telegraph: its finish and its fired broadcast share one fresh id.</summary>
    [Fact]
    public void Name_an_instant_casts_finish_and_fired_broadcast_with_one_id()
    {
        ICreature caster = Creature(Vector3.zero);
        GameAbility cleave = AbilityTestData.Game(AbilityTestData.Cone(200, reach: 4f, arc: 90f));

        Assert.True(_sut.RunInstant(caster, new AbilityAim(new Vector3(0f, 0f, 1f), null), cleave));

        Assert.Empty(_arena.Started);
        (AbilityFootprint fired, uint firedId) = Assert.Single(_arena.FiredFootprints);
        Assert.NotEqual(0u, firedId);
        Assert.Equal([firedId], _arena.FinishedIds);
        Assert.Equal((4f, 90f), (fired.Reach, fired.ArcDegrees));
    }

    [Fact]
    public void Broadcast_no_start_for_a_cast_the_queue_refuses()
    {
        ICreature caster = Creature(Vector3.zero);
        Avalon.Domain.World.AbilityTemplate template = AbilityTestData.Circle(1);
        template.ScriptName = "Nope";
        GameAbility missing = Timed(template, castTimeMs: 100);

        Assert.False(_sut.QueueAbility(caster, new AbilityAim(new Vector3(0f, 0f, 1f), null), missing));

        Assert.Empty(_arena.Started);
    }
}
