using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class CreatureRespawnerShould
{
    // Remove timer = 2 min, respawn timer = 3 min (from CreatureRespawner source)
    private static readonly TimeSpan RemoveInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RespawnInterval = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private readonly IChunk _chunk = Substitute.For<IChunk>();
    private readonly ICreature _creature = Substitute.For<ICreature>();

    // ──────────────────────────────────────────────
    // ScheduleRespawn
    // ──────────────────────────────────────────────

    [Fact]
    public void NotCallChunk_WhenJustScheduled()
    {
        var respawner = new CreatureRespawner(_chunk);
        respawner.ScheduleRespawn(_creature);

        _chunk.DidNotReceive().RemoveCreature(Arg.Any<ICreature>());
        _chunk.DidNotReceive().RespawnCreature(Arg.Any<ICreature>());
    }

    [Fact]
    public void NotCallChunk_WhenDeltaIsBelowBothThresholds()
    {
        var respawner = new CreatureRespawner(_chunk);
        respawner.ScheduleRespawn(_creature);

        respawner.Update(TimeSpan.FromSeconds(30)); // well below 2 min

        _chunk.DidNotReceive().RemoveCreature(Arg.Any<ICreature>());
        _chunk.DidNotReceive().RespawnCreature(Arg.Any<ICreature>());
    }

    // ──────────────────────────────────────────────
    // Remove timer (2 min)
    // ──────────────────────────────────────────────

    [Fact]
    public void RemoveCreature_WhenRemoveTimerExpires()
    {
        var respawner = new CreatureRespawner(_chunk);
        respawner.ScheduleRespawn(_creature);

        respawner.Update(RemoveInterval + Tick);

        _chunk.Received(1).RemoveCreature(_creature);
    }

    [Fact]
    public void NotRespawn_WhenOnlyRemoveTimerExpires()
    {
        var respawner = new CreatureRespawner(_chunk);
        respawner.ScheduleRespawn(_creature);

        // Past 2 min but not 3 min
        respawner.Update(RemoveInterval + Tick);

        _chunk.DidNotReceive().RespawnCreature(Arg.Any<ICreature>());
    }

    [Fact]
    public void NotRemoveTwice_AfterTimerAlreadyFired()
    {
        var respawner = new CreatureRespawner(_chunk);
        respawner.ScheduleRespawn(_creature);

        respawner.Update(RemoveInterval + Tick); // fires once
        respawner.Update(RemoveInterval + Tick); // timer removed from dict

        _chunk.Received(1).RemoveCreature(_creature); // still only once
    }

    // ──────────────────────────────────────────────
    // Respawn timer (3 min)
    // ──────────────────────────────────────────────

    [Fact]
    public void RespawnCreature_WhenRespawnTimerExpires()
    {
        var respawner = new CreatureRespawner(_chunk);
        respawner.ScheduleRespawn(_creature);

        respawner.Update(RespawnInterval + Tick); // past both timers

        _chunk.Received(1).RespawnCreature(_creature);
    }

    [Fact]
    public void NotRespawnTwice_AfterTimerAlreadyFired()
    {
        var respawner = new CreatureRespawner(_chunk);
        respawner.ScheduleRespawn(_creature);

        respawner.Update(RespawnInterval + Tick);
        respawner.Update(RespawnInterval + Tick);

        _chunk.Received(1).RespawnCreature(_creature);
    }

    // ──────────────────────────────────────────────
    // Multiple creatures
    // ──────────────────────────────────────────────

    [Fact]
    public void RemoveAllCreatures_WhenMultipleScheduledAndTimerExpires()
    {
        var creature2 = Substitute.For<ICreature>();
        var creature3 = Substitute.For<ICreature>();
        var respawner = new CreatureRespawner(_chunk);
        respawner.ScheduleRespawn(_creature);
        respawner.ScheduleRespawn(creature2);
        respawner.ScheduleRespawn(creature3);

        respawner.Update(RemoveInterval + Tick);

        _chunk.Received(1).RemoveCreature(_creature);
        _chunk.Received(1).RemoveCreature(creature2);
        _chunk.Received(1).RemoveCreature(creature3);
    }

    [Fact]
    public void RespawnAllCreatures_WhenMultipleScheduledAndTimerExpires()
    {
        var creature2 = Substitute.For<ICreature>();
        var respawner = new CreatureRespawner(_chunk);
        respawner.ScheduleRespawn(_creature);
        respawner.ScheduleRespawn(creature2);

        respawner.Update(RespawnInterval + Tick);

        _chunk.Received(1).RespawnCreature(_creature);
        _chunk.Received(1).RespawnCreature(creature2);
    }
}
