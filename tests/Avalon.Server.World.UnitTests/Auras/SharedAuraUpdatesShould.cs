using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Auras;
using Avalon.World.Instances;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>#980: a unit's aura update, encoded once per broadcast pass and shared by every recipient.</summary>
public class SharedAuraUpdatesShould
{
    private static readonly DateTimeOffset s_t0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);

    /// <summary>
    /// Through a real instance's broadcast: one aura update reaches the caster, the unit and a watcher as one payload,
    /// each recipient's own reference to it, and the instance gives up its own when the broadcast ends, also when the
    /// broadcast meets a throw partway (a recipient's send refuses the update, letting go of it, and throws; contained per
    /// recipient since #981, so the recipients after it are still sent theirs). So every payload
    /// the broadcast encoded is still out while a recipient holds it, and none is once they have all let go. An
    /// instance that never gave its reference back would keep the update out of the pool for good; one that gave it
    /// back while a recipient still held it would let the pool hand that recipient's bytes to the next packet.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Give_a_broadcasts_shared_aura_update_back_once_its_recipients_have(bool broadcastThrows)
    {
        var encoder = new PacketEncoder(new PayloadSegmentPool(countOutstanding: true));
        var clock = new FakeTimeProvider(s_t0);
        using MapInstance instance = TestMapInstances.Build(
            NewWorld(await TestStaticData.LoadAsync(TestStaticData.Repositories(auras: () => [AuraTestData.Renew()]))),
            time: clock, encoder: encoder);
        MapInstanceClient caster = Player(instance, 980_170, 0f);
        MapInstanceClient target = Player(instance, 980_171, 4f);
        MapInstanceClient watcher = Player(instance, 980_172, 8f);
        MapInstanceClient[] clients = [caster, target, watcher];
        instance.Update(s_tick);
        LetGo(clients);
        Assert.Equal(0, encoder.Pool.Outstanding);

        if (broadcastThrows)
        {
            target.Connection.When(c => c.Send(Arg.Is<OutboundPacket>(p => p.Header.Type == NetworkPacketType.SMSG_AURA_UPDATE)))
                .Do(call =>
                {
                    OutboundPacket refused = call.Arg<OutboundPacket>();
                    target.Sent.Remove(refused);
                    refused.Release();
                    throw new InvalidOperationException("The connection refused the packet.");
                });
        }

        instance.Auras.Apply(caster.Character, target.Character, new AuraId(904), AuraSource.None);
        instance.Update(s_tick);

        MapInstanceClient[] told = broadcastThrows ? [caster, watcher] : clients;
        PayloadSegment shared = Assert.Single(told
            .Select(c => Assert.Single(c.Sent, p => p.Header.Type == NetworkPacketType.SMSG_AURA_UPDATE).Payload!)
            .Distinct());
        Assert.Equal(Held(clients).Count, encoder.Pool.Outstanding);
        Assert.Contains(shared, Held(clients));
        LetGo(clients);
        Assert.Equal(0, encoder.Pool.Outstanding);
    }

    private static MapInstanceClient Player(MapInstance instance, uint id, float z)
    {
        MapInstanceClient client = Join(instance, Inventory.TestCharacters.New(id));
        client.Character.Health = 500;
        client.Character.CurrentHealth = 500;
        client.Character.Position = new Vector3(0f, 0f, z);
        return client;
    }

    /// <summary>The payloads the clients still hold, each once however many of them hold it.</summary>
    private static List<PayloadSegment> Held(MapInstanceClient[] clients) =>
        [.. clients.SelectMany(c => c.Sent).Where(p => p.Payload is not null).Select(p => p.Payload!).Distinct()];

    /// <summary>Every client lets go of what it was sent, as a connection does once it has framed it.</summary>
    private static void LetGo(MapInstanceClient[] clients)
    {
        foreach (MapInstanceClient client in clients)
        {
            foreach (OutboundPacket packet in client.Sent)
                packet.Release();
            client.Sent.Clear();
        }
    }
}
