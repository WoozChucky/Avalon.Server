using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class CreatureCorpseRemoverShould
{
    /// <summary>The shipped default, short because a corpse is still a ticked, broadcast entity.</summary>
    private static readonly TimeSpan DefaultRemove = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private readonly ISimulationContext _simulationContext = Substitute.For<ISimulationContext>();

    private static ICreature MakeCreature(TimeSpan? remove = null)
    {
        var creature = Substitute.For<ICreature>();
        var metadata = Substitute.For<ICreatureMetadata>();
        metadata.BodyRemoveTimer.Returns(remove ?? DefaultRemove);
        creature.Metadata.Returns(metadata);
        return creature;
    }

    [Fact]
    public void Leave_A_Corpse_Alone_When_Just_Scheduled()
    {
        ICreature creature = MakeCreature();
        var remover = new CreatureCorpseRemover(_simulationContext);

        remover.ScheduleRemoval(creature);

        _simulationContext.DidNotReceive().RemoveCreature(Arg.Any<ICreature>());
    }

    [Fact]
    public void Leave_A_Corpse_Alone_Before_Its_Timer_Elapses()
    {
        ICreature creature = MakeCreature();
        var remover = new CreatureCorpseRemover(_simulationContext);
        remover.ScheduleRemoval(creature);

        remover.Update(DefaultRemove - TimeSpan.FromSeconds(1));

        _simulationContext.DidNotReceive().RemoveCreature(Arg.Any<ICreature>());
    }

    [Fact]
    public void Remove_A_Corpse_Once_Its_Timer_Elapses()
    {
        ICreature creature = MakeCreature();
        var remover = new CreatureCorpseRemover(_simulationContext);
        remover.ScheduleRemoval(creature);

        remover.Update(DefaultRemove + Tick);

        _simulationContext.Received(1).RemoveCreature(creature);
    }

    [Fact]
    public void Remove_A_Corpse_Only_Once()
    {
        ICreature creature = MakeCreature();
        var remover = new CreatureCorpseRemover(_simulationContext);
        remover.ScheduleRemoval(creature);

        remover.Update(DefaultRemove + Tick);
        remover.Update(DefaultRemove + Tick);

        _simulationContext.Received(1).RemoveCreature(creature);
    }

    [Fact]
    public void Remove_Every_Corpse_Whose_Timer_Elapsed()
    {
        ICreature first = MakeCreature();
        ICreature second = MakeCreature();
        ICreature third = MakeCreature();
        var remover = new CreatureCorpseRemover(_simulationContext);
        remover.ScheduleRemoval(first);
        remover.ScheduleRemoval(second);
        remover.ScheduleRemoval(third);

        remover.Update(DefaultRemove + Tick);

        _simulationContext.Received(1).RemoveCreature(first);
        _simulationContext.Received(1).RemoveCreature(second);
        _simulationContext.Received(1).RemoveCreature(third);
    }

    /// <summary>
    /// The timer is per-template so something that should linger — a boss, say — can be given longer
    /// than the trash around it. A hardcoded interval would silently ignore that column.
    /// </summary>
    [Fact]
    public void Honour_The_Templates_Timer_Rather_Than_A_Hardcoded_One()
    {
        ICreature lingers = MakeCreature(remove: TimeSpan.FromSeconds(60));
        var remover = new CreatureCorpseRemover(_simulationContext);
        remover.ScheduleRemoval(lingers);

        remover.Update(DefaultRemove + Tick);
        _simulationContext.DidNotReceive().RemoveCreature(lingers);

        remover.Update(TimeSpan.FromSeconds(61));
        _simulationContext.Received(1).RemoveCreature(lingers);
    }
}
