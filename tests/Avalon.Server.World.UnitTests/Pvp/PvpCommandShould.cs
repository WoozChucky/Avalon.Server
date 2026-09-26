using System.IO;
using Avalon.Common.Accounts;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Pvp;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;
using Xunit;

namespace Avalon.Server.World.UnitTests.Pvp;

/// <summary>/pvp and CMSG_PVP_TOGGLE are one path (#164): both call PvpToggle.Toggle and reply with SMSG_PVP_STATE.</summary>
public class PvpCommandShould
{
    private readonly List<NetworkPacket> _sent = [];
    private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
    private readonly PvpCommand _command;
    private readonly PvpToggleHandler _handler;

    public PvpCommandShould()
    {
        var toggle = new PvpToggle(Options.Create(new GameConfiguration { PvpOffDelay = TimeSpan.FromMinutes(5) }),
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero)));
        _command = new PvpCommand(toggle);
        _handler = new PvpToggleHandler(toggle);

        _connection.Character.Returns(TestCharacters.New(1));
        _connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        _connection.When(c => c.Send(Arg.Any<NetworkPacket>())).Do(ci => _sent.Add(ci.Arg<NetworkPacket>()));
    }

    private List<SPvpStatePacket> Replies() => _sent
        .Where(p => p.Header.Type == NetworkPacketType.SMSG_PVP_STATE)
        .Select(p =>
        {
            using var stream = new MemoryStream(p.Payload);
            return Serializer.Deserialize<SPvpStatePacket>(stream);
        })
        .ToList();

    [Fact]
    public async Task Agree_with_the_packet_because_both_run_one_path()
    {
        await _command.ExecuteAsync(
            new WorldPacketContext<CChatMessagePacket> { Connection = _connection, Packet = new CChatMessagePacket() }, []);
        _handler.Execute(_connection, new CPvpTogglePacket());

        List<SPvpStatePacket> replies = Replies();
        Assert.Equal([new PvpStatus(true, 0u), new PvpStatus(true, 300_000u)],
            replies.Select(r => new PvpStatus(r.Enabled, r.OffInMs)));
    }

    [Fact]
    public void Be_named_pvp() => Assert.Equal("pvp", _command.Name);

    [Fact]
    public void Be_runnable_by_every_player() =>
        Assert.Equal(AccessLevels.Player, ((ICommand)_command).RequiredAccess);

    [Fact]
    public void Answer_nothing_for_a_connection_with_no_character()
    {
        _connection.Character.Returns((ICharacter?)null);
        _handler.Execute(_connection, new CPvpTogglePacket());
        Assert.Empty(Replies());
    }
}
