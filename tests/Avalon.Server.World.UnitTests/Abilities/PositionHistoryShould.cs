using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Hosting.Networking;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Maps;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Abilities;

/// <summary>
/// A unit's recent positions by world tick (#1082): a rewound query finds where the unit stood at the tick a client saw,
/// and never a position from before a jump.
/// </summary>
public class PositionHistoryShould
{
    public enum Story
    {
        /// <summary>Seeded at tick 100 at x 1, moved to x 2 at tick 110.</summary>
        SeededThenMoved,

        /// <summary>At x = tick on every tick from 100 to 130.</summary>
        MovedEveryTick,

        /// <summary>Seeded at tick 100 at x 1, the same position recorded every tick to 130, then x 2 at 131.</summary>
        StoodStillThenMoved,

        /// <summary>At x 1 and then x 2, both recorded on tick 100.</summary>
        TwiceInOneTick,

        /// <summary>Seeded at tick 100 at x 1, x 2 at tick 110, then a jump, then x 3 at tick 120.</summary>
        Jumped,
    }

    [Theory]
    [InlineData(Story.SeededThenMoved, 105L, 1f)]
    [InlineData(Story.SeededThenMoved, 110L, 2f)]
    [InlineData(Story.SeededThenMoved, 99L, null)]
    [InlineData(Story.MovedEveryTick, 130L - 15L, 115f)]          // the rewind cap, inside the 18 entries
    [InlineData(Story.StoodStillThenMoved, 105L, 1f)]             // a still unit keeps its seed past 18 ticks
    [InlineData(Story.TwiceInOneTick, 100L, 2f)]                  // the last position of the tick
    [InlineData(Story.Jumped, 110L, null)]                        // nothing from before the jump survives
    [InlineData(Story.Jumped, 120L, 3f)]
    public void Answer_the_newest_position_at_or_before_a_tick(Story story, long tick, float? expectedX)
    {
        var history = new PositionHistory();
        switch (story)
        {
            case Story.SeededThenMoved:
                history.Record(100, At(1f));
                history.Record(110, At(2f));
                break;
            case Story.MovedEveryTick:
                for (long t = 100; t <= 130; t++)
                    history.Record(t, At(t));
                break;
            case Story.StoodStillThenMoved:
                for (long t = 100; t <= 130; t++)
                    history.Record(t, At(1f));
                history.Record(131, At(2f));
                break;
            case Story.TwiceInOneTick:
                history.Record(100, At(1f));
                history.Record(100, At(2f));
                break;
            case Story.Jumped:
                history.Record(100, At(1f));
                history.Record(110, At(2f));
                history.Invalidate();
                history.Record(120, At(3f));
                break;
        }

        bool found = history.TryGet(tick, out Vector3 position);

        Assert.Equal(expectedX is not null, found);
        if (expectedX is { } x)
            Assert.Equal(At(x), position);
    }

    /// <summary>
    /// A creature teleported mid-walk (a leash return) has no position from before the teleport: a query rewound to a
    /// tick before it finds nothing, so it uses the creature's present position, never one it was teleported off.
    /// </summary>
    [Fact]
    public void Forget_a_creatures_walk_once_it_is_teleported()
    {
        var ticks = new WorldTicks(1_000);
        IMapNavigator navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(default, default).ReturnsForAnyArgs(ci => new List<Vector3> { ci.ArgAt<Vector3>(1) });
        using MapInstance instance = TestMapInstances.Build(NewWorld(), navigator: navigator, worldTicks: ticks);
        Join(instance, 916_101);   // an instance with no one in it does not tick
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 916_901),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = new Vector3(0f, 0f, 3f),
            Speed = 6f,
            Level = 1,
            Health = 100,
            CurrentHealth = 100,
        };
        instance.AddCreature(boar);
        instance.Locomotion.MoveTo(boar, new Vector3(20f, 0f, 3f));
        for (int i = 0; i < 5; i++)
            Tick(instance, ticks);
        PositionHistory history = Assert.IsType<PositionHistory>(boar.History);
        Assert.True(history.TryGet(1_003, out Vector3 walking));
        Assert.NotEqual(new Vector3(0f, 0f, 3f), walking);   // it was walking

        var home = new Vector3(-10f, 0f, 3f);
        instance.Locomotion.Teleport(boar, home);
        Tick(instance, ticks);   // tick 1005

        Assert.False(history.TryGet(1_004, out _));
        Assert.True(history.TryGet(1_005, out Vector3 now));
        Assert.Equal(home, now);
    }

    private static void Tick(MapInstance instance, WorldTicks ticks)
    {
        instance.Update(TimeSpan.FromSeconds(1d / 60d));
        ticks.Advance();
    }

    private static Vector3 At(float x) => new(x, 0f, 0f);
}
