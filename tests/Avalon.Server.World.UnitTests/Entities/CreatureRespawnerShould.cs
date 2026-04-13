using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class CreatureRespawnerShould
{
    private static readonly TimeSpan DefaultRemove  = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultRespawn = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Tick           = TimeSpan.FromMilliseconds(1);

    private readonly ISimulationContext _simulationContext = Substitute.For<ISimulationContext>();

    /// <summary>Creates a mock creature whose metadata returns the given timer values.</summary>
    private static ICreature MakeCreature(
        TimeSpan? respawn = null,
        TimeSpan? remove  = null)
    {
        var creature = Substitute.For<ICreature>();
        var metadata = Substitute.For<ICreatureMetadata>();
        metadata.RespawnTimer.Returns(respawn ?? DefaultRespawn);
        metadata.BodyRemoveTimer.Returns(remove  ?? DefaultRemove);
        creature.Metadata.Returns(metadata);
        return creature;
    }

    // ──────────────────────────────────────────────
    // ScheduleRespawn
    // ──────────────────────────────────────────────

    [Fact]
    public void NotCallSimulationContext_WhenJustScheduled()
    {
        var creature  = MakeCreature();
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature);

        _simulationContext.DidNotReceive().RemoveCreature(Arg.Any<ICreature>());
        _simulationContext.DidNotReceive().RespawnCreature(Arg.Any<ICreature>());
    }

    [Fact]
    public void NotCallSimulationContext_WhenDeltaIsBelowBothThresholds()
    {
        var creature  = MakeCreature();
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature);

        respawner.Update(TimeSpan.FromSeconds(30));

        _simulationContext.DidNotReceive().RemoveCreature(Arg.Any<ICreature>());
        _simulationContext.DidNotReceive().RespawnCreature(Arg.Any<ICreature>());
    }

    // ──────────────────────────────────────────────
    // Remove timer
    // ──────────────────────────────────────────────

    [Fact]
    public void RemoveCreature_WhenRemoveTimerExpires()
    {
        var creature  = MakeCreature();
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature);

        respawner.Update(DefaultRemove + Tick);

        _simulationContext.Received(1).RemoveCreature(creature);
    }

    [Fact]
    public void NotRespawn_WhenOnlyRemoveTimerExpires()
    {
        var creature  = MakeCreature();
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature);

        respawner.Update(DefaultRemove + Tick);

        _simulationContext.DidNotReceive().RespawnCreature(Arg.Any<ICreature>());
    }

    [Fact]
    public void NotRemoveTwice_AfterTimerAlreadyFired()
    {
        var creature  = MakeCreature();
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature);

        respawner.Update(DefaultRemove + Tick);
        respawner.Update(DefaultRemove + Tick);

        _simulationContext.Received(1).RemoveCreature(creature);
    }

    // ──────────────────────────────────────────────
    // Respawn timer
    // ──────────────────────────────────────────────

    [Fact]
    public void RespawnCreature_WhenRespawnTimerExpires()
    {
        var creature  = MakeCreature();
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature);

        respawner.Update(DefaultRespawn + Tick);

        _simulationContext.Received(1).RespawnCreature(creature);
    }

    [Fact]
    public void NotRespawnTwice_AfterTimerAlreadyFired()
    {
        var creature  = MakeCreature();
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature);

        respawner.Update(DefaultRespawn + Tick);
        respawner.Update(DefaultRespawn + Tick);

        _simulationContext.Received(1).RespawnCreature(creature);
    }

    // ──────────────────────────────────────────────
    // Multiple creatures
    // ──────────────────────────────────────────────

    [Fact]
    public void RemoveAllCreatures_WhenMultipleScheduledAndTimerExpires()
    {
        var creature1 = MakeCreature();
        var creature2 = MakeCreature();
        var creature3 = MakeCreature();
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature1);
        respawner.ScheduleRespawn(creature2);
        respawner.ScheduleRespawn(creature3);

        respawner.Update(DefaultRemove + Tick);

        _simulationContext.Received(1).RemoveCreature(creature1);
        _simulationContext.Received(1).RemoveCreature(creature2);
        _simulationContext.Received(1).RemoveCreature(creature3);
    }

    [Fact]
    public void RespawnAllCreatures_WhenMultipleScheduledAndTimerExpires()
    {
        var creature1 = MakeCreature();
        var creature2 = MakeCreature();
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature1);
        respawner.ScheduleRespawn(creature2);

        respawner.Update(DefaultRespawn + Tick);

        _simulationContext.Received(1).RespawnCreature(creature1);
        _simulationContext.Received(1).RespawnCreature(creature2);
    }

    // ──────────────────────────────────────────────
    // Template-defined timers (TODO-021/023)
    // ──────────────────────────────────────────────

    [Fact]
    public void UsesTemplateRemoveTimer_NotHardcodedDefault()
    {
        // 30 s template timer fires well before the hardcoded default of 120 s
        var creature  = MakeCreature(remove: TimeSpan.FromSeconds(30));
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature);

        respawner.Update(TimeSpan.FromSeconds(31));

        _simulationContext.Received(1).RemoveCreature(creature);
    }

    [Fact]
    public void UsesTemplateRespawnTimer_NotHardcodedDefault()
    {
        // 60 s template timer fires well before the hardcoded default of 180 s
        var creature  = MakeCreature(respawn: TimeSpan.FromSeconds(60), remove: TimeSpan.FromSeconds(10));
        var respawner = new CreatureRespawner(_simulationContext);
        respawner.ScheduleRespawn(creature);

        respawner.Update(TimeSpan.FromSeconds(61));

        _simulationContext.Received(1).RespawnCreature(creature);
    }
}
