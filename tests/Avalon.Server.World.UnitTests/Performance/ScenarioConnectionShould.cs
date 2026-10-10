using Avalon.Configuration;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Testing.Scenarios;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Performance;

public class ScenarioConnectionShould
{
    // What AES-GCM sealing adds to a payload: the 12-byte nonce in front, the 16-byte tag behind.
    private const int NonceSize = 12;
    private const int TagSize = 16;

    [Fact]
    public void Encrypt_frame_and_write_what_it_sends()
    {
        // One send thread, never started: the test runs its pass.
        using var scheduler = new NetworkSendScheduler(new NetworkConfiguration { SendThreads = 1 },
            NullLoggerFactory.Instance, TimeProvider.System, NetworkSendMetrics.Disabled);
        var sealedConnection = new ScenarioConnection(TestCharacters.New(650_001), scheduler);
        var plainConnection = new ScenarioConnection(TestCharacters.New(650_002), scheduler, sealPayloads: false);

        sealedConnection.Send(SPlayerStateAckPacket.Create(1, 2f, 3f, 4f, 0f, 0f, 90, PacketEncoder.Shared));
        plainConnection.Send(SPlayerStateAckPacket.Create(1, 2f, 3f, 4f, 0f, 0f, 90, PacketEncoder.Shared));
        Assert.Equal(0, sealedConnection.BytesWritten); // queued, not written

        scheduler.RunAllPasses();

        // The same packet framed both ways: only a real seal adds the nonce and the tag.
        Assert.True(plainConnection.BytesWritten > 0);
        Assert.True(sealedConnection.BytesWritten >= plainConnection.BytesWritten + NonceSize + TagSize,
            $"sealed {sealedConnection.BytesWritten} bytes, plain {plainConnection.BytesWritten}");
    }

    [Fact]
    public void Tick_the_idle_town_and_send_state_to_its_players()
    {
        using ScenarioWorld world = new TownIdleScenario().Build();
        for (int i = 0; i < 30; i++) world.Tick(); // > one 0.1 s broadcast
        Assert.All(world.Connections, c => Assert.True(c.BytesWritten > 0));
    }
}
