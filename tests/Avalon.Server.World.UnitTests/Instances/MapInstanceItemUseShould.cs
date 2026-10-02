using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.World.Instances;
using Avalon.World.Items;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>MapInstance hosts item cast bars with its own cast ids and the existing cast packets.</summary>
public class MapInstanceItemUseShould
{
    private static readonly ItemTemplateId Scroll = new(3);

    private static PendingItemUse Pending(MapInstanceClient client, List<string> ends, float seconds = 3f) => new()
    {
        Character = client.Character, Item = Scroll, StartPosition = client.Character.Position,
        CastId = 0, CastTimeSeconds = seconds, CanComplete = () => true,
        Completed = () => ends.Add("completed"), Interrupted = () => ends.Add("interrupted"),
    };

    [Fact]
    public void Number_item_casts_from_the_instances_own_cast_ids()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());

        uint first = instance.ItemUses.TakeCastId();
        uint second = instance.ItemUses.TakeCastId();

        Assert.NotEqual(0u, first);
        Assert.Equal(first + 1, second);
    }

    [Fact]
    public void Send_the_start_and_finish_naming_the_item_and_complete_on_its_tick()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient client = Join(instance, 7);
        var ends = new List<string>();
        uint castId = instance.ItemUses.TakeCastId();
        instance.ItemUses.Start(new PendingItemUse
        {
            Character = client.Character, Item = Scroll, StartPosition = client.Character.Position, CastId = castId,
            CastTimeSeconds = 3f, CanComplete = () => true,
            Completed = () => ends.Add("completed"), Interrupted = () => ends.Add("interrupted"),
        });

        instance.Update(TimeSpan.FromSeconds(3.1));

        SUnitStartCastPacket start = Assert.Single(client.Read<SUnitStartCastPacket>(NetworkPacketType.SMSG_UNIT_START_CAST));
        Assert.Equal((3ul, 0u, castId, 3f), (start.ItemTemplateId, start.AbilityId, start.CastId, start.CastTime));
        SUnitFinishCastPacket finish = Assert.Single(client.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
        Assert.Equal((3ul, castId), (finish.ItemTemplateId, finish.CastId));
        Assert.Equal(["completed"], ends);
    }

    /// <summary>Leaving (a transfer, a logout) ends the bar for every watcher and answers the use.</summary>
    [Fact]
    public void Interrupt_an_item_cast_when_its_caster_leaves()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient caster = Join(instance, 7);
        MapInstanceClient watcher = Join(instance, 8);
        var ends = new List<string>();
        instance.ItemUses.Start(Pending(caster, ends));

        instance.RemoveCharacter(caster.Connection);

        Assert.Equal(["interrupted"], ends);
        Assert.False(instance.ItemUses.IsCasting(caster.Character.Guid));
        SCharacterInterruptedCastPacket heard =
            Assert.Single(watcher.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST));
        Assert.Equal(3ul, heard.ItemTemplateId);
    }

    [Fact]
    public void Restore_health_through_its_own_combat_service()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient client = Join(instance, 7);
        client.Character.Health = 100;
        client.Character.CurrentHealth = 50;

        uint restored = ((IItemUseHost)instance).RestoreHealth(client.Character, client.Character, 30);

        Assert.Equal(30u, restored);
        Assert.Equal(80u, client.Character.CurrentHealth);
        Assert.Single(client.Read<SUnitHealedPacket>(NetworkPacketType.SMSG_UNIT_HEALED));
    }
}
