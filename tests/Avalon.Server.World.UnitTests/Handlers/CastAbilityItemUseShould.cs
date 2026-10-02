using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Items;
using Xunit;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>Item use: an ability cast the cast system accepts ends a running item cast bar, out loud.</summary>
public class CastAbilityItemUseShould
{
    [Fact]
    public void End_the_item_cast_when_an_ability_cast_is_accepted()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient client = MapInstanceClients.Join(instance, 7);
        client.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Circle(1))]);
        client.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-10);
        var ends = new List<string>();
        instance.ItemUses.Start(new PendingItemUse
        {
            Character = client.Character, Item = new ItemTemplateId(3), StartPosition = client.Character.Position,
            CastId = instance.ItemUses.TakeCastId(), CastTimeSeconds = 3f, CanComplete = () => true,
            Completed = () => ends.Add("completed"), Interrupted = () => ends.Add("interrupted"),
        });

        handler.Execute(client.Connection, new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(["interrupted"], ends);
        Assert.Contains(client.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST),
            p => p.ItemTemplateId == 3ul);
    }

    /// <summary>A refused ability cast (the global cooldown here) leaves the item cast running.</summary>
    [Fact]
    public void Keep_the_item_cast_when_an_ability_cast_is_refused()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient client = MapInstanceClients.Join(instance, 7);
        client.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Circle(1))]);
        client.Character.LastCastStartTime = DateTime.UtcNow;
        var ends = new List<string>();
        instance.ItemUses.Start(new PendingItemUse
        {
            Character = client.Character, Item = new ItemTemplateId(3), StartPosition = client.Character.Position,
            CastId = instance.ItemUses.TakeCastId(), CastTimeSeconds = 3f, CanComplete = () => true,
            Completed = () => ends.Add("completed"), Interrupted = () => ends.Add("interrupted"),
        });

        handler.Execute(client.Connection, new CCastAbilityPacket { AbilityId = 1 });

        Assert.Equal(CastRejectReason.Gcd,
            Assert.Single(client.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY)).Reason);
        Assert.Empty(ends);
        Assert.True(instance.ItemUses.IsCasting(client.Character.Guid));
    }
}
