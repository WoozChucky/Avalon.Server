using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Items;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// Item use: an ability cast the cast system accepts ends a running item cast bar, out loud; a refused one (the global
/// cooldown here) leaves it running.
/// </summary>
public class CastAbilityItemUseShould
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void End_the_item_cast_only_when_an_ability_cast_is_accepted(bool accepted)
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient client = MapInstanceClients.Join(instance, 7);
        client.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Circle(1))]);
        client.Character.LastCastStartTime = accepted ? DateTime.UtcNow.AddSeconds(-10) : DateTime.UtcNow;
        var ends = new List<string>();
        instance.ItemUses.Start(new PendingItemUse
        {
            Character = client.Character,
            Item = new ItemTemplateId(3),
            StartPosition = client.Character.Position,
            CastId = instance.ItemUses.TakeCastId(),
            CastTimeSeconds = 3f,
            CanComplete = () => true,
            Completed = () => ends.Add("completed"),
            Interrupted = () => ends.Add("interrupted"),
        });

        handler.Execute(client.Connection, new CCastAbilityPacket { AbilityId = 1 });

        string[] expectedEnds = accepted ? ["interrupted"] : [];
        CastRejectReason[] expectedRefusals = accepted ? [] : [CastRejectReason.Gcd];
        Assert.Equal(expectedEnds, ends);
        Assert.Equal(expectedRefusals,
            client.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY).Select(p => p.Reason));
        Assert.Equal(accepted, client.Read<SCharacterInterruptedCastPacket>(NetworkPacketType.SMSG_INTERRUPTED_CAST)
            .Any(p => p.ItemTemplateId == 3ul));
        Assert.Equal(!accepted, instance.ItemUses.IsCasting(client.Character.Guid));
    }
}
