using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #648 end to end, through a real MapInstance and the real cast handler: a watcher that knows none of the
/// caster's abilities draws the telegraph from the start broadcast alone, and clears it on the finish or the
/// interrupt carrying the same cast id. Character ids 648_101 to 648_199 are unique to this class.
/// </summary>
public class CastTelegraphBroadcastShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    private static AbilityTemplate Timed(AbilityTemplate template, uint castTimeMs)
    {
        template.CastTime = castTimeMs;
        return template;
    }

    [Fact]
    public void Tell_a_watcher_where_a_cast_will_land_and_clear_it_on_the_same_casts_finish()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient wizard = Join(instance, 648_101);
        MapInstanceClient watcher = Join(instance, 648_102);
        wizard.Character.Spells.Load(
            [AbilityTestData.Game(Timed(AbilityTestData.AimedCircle(211, reach: 8f, radius: 3f), castTimeMs: 100))]);

        handler.Execute(wizard.Connection,
            new CCastAbilityPacket { AbilityId = 211, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 20f } });

        SUnitStartCastPacket start = Assert.Single(watcher.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
        Assert.NotEqual(0u, start.CastId);
        AbilityFootprintDto telegraph = start.Footprint!;
        Assert.Equal(AbilityShape.Circle, telegraph.Shape);
        Assert.Equal(8f, telegraph.Centre!.Z, 3);   // clamped to the reach, not the 20 m it was aimed at
        Assert.Equal(3f, telegraph.Radius);
        Assert.Null(telegraph.Direction);

        for (int i = 0; i < 12; i++)
        {
            instance.Update(Tick);
        }

        SAbilityFiredPacket fired = Assert.Single(watcher.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED));
        Assert.Equal(start.CastId, fired.CastId);
        Assert.Equal(telegraph.Centre.Z, fired.Footprint!.Centre!.Z);
        Assert.Equal(telegraph.Radius, fired.Footprint.Radius);
        Assert.Equal(fired.Footprint.Centre.Z, fired.Centre!.Z);   // the old members still carry it
        SUnitFinishCastPacket finish = Assert.Single(watcher.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
        Assert.Equal(start.CastId, finish.CastId);
    }

    [Fact]
    public void Clear_a_telegraph_on_the_interrupt_that_names_its_cast()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Join(instance, 648_111);
        MapInstanceClient watcher = Join(instance, 648_112);
        warrior.Character.Spells.Load(
            [AbilityTestData.Game(Timed(AbilityTestData.Cone(212, reach: 5f, arc: 60f), castTimeMs: 1000))]);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 212 });
        warrior.Character.Position = new Vector3(1f, 0f, 0f);   // a character that moves is interrupted
        instance.Update(Tick);

        SUnitStartCastPacket start = Assert.Single(watcher.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
        Assert.Equal((AbilityShape.Cone, 5f, 60f), (start.Footprint!.Shape, start.Footprint.Reach, start.Footprint.ArcDegrees));
        Assert.NotNull(start.Footprint.Direction);
        SCharacterInterruptedCastPacket interrupt =
            Assert.Single(watcher.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        Assert.Equal(start.CastId, interrupt.CastId);
        Assert.Empty(watcher.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED));
    }

    /// <summary>An instant cast sends no start: its fired broadcast carries the whole footprint and its finish the same id.</summary>
    [Fact]
    public void Carry_an_instant_casts_whole_footprint_on_its_fired_broadcast()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Join(instance, 648_121);
        MapInstanceClient watcher = Join(instance, 648_122);
        warrior.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Cone(200, reach: 4f, arc: 90f))]);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Empty(watcher.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
        SAbilityFiredPacket fired = Assert.Single(watcher.Read<SAbilityFiredPacket>(NetworkPacketType.SMSG_ABILITY_FIRED));
        Assert.NotEqual(0u, fired.CastId);
        Assert.Equal((AbilityShape.Cone, 4f, 90f), (fired.Footprint!.Shape, fired.Footprint.Reach, fired.Footprint.ArcDegrees));
        SUnitFinishCastPacket finish = Assert.Single(watcher.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
        Assert.Equal(fired.CastId, finish.CastId);
    }
}
