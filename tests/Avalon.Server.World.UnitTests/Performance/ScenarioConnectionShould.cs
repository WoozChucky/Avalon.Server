using Avalon.Network.Packets.Movement;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Server.World.UnitTests.Performance;

public class ScenarioConnectionShould
{
    [Fact]
    public void Encrypt_frame_and_write_what_it_sends()
    {
        CharacterEntity character = TestCharacters.New(650_001);
        var connection = new ScenarioConnection(character);

        connection.Send(SPlayerStateAckPacket.Create(1, 2f, 3f, 4f, 0f, 0f, 90, connection.CryptoSession.Encrypt));
        Assert.Equal(0, connection.BytesWritten); // queued, not written
        connection.FlushOutbox();

        Assert.True(connection.BytesWritten > 0); // encrypted and framed bytes reached the stream
    }
}
