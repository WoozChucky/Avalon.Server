using System.IO;
using Avalon.Common.Accounts;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;
using Xunit;
using Avalon.Combat;

namespace Avalon.Server.World.UnitTests.Chat;

public class GodModeCommandShould
{
    [Theory]
    [InlineData(AccountAccessLevel.GameMaster)]
    [InlineData(AccountAccessLevel.Admin)]
    public async Task Enable_and_disable_the_invoking_staff_character(AccountAccessLevel level)
    {
        var fixture = new Fixture(level);

        Assert.False(fixture.Character.GodMode);
        Assert.True(await fixture.Dispatch("/god on"));
        Assert.True(fixture.Character.GodMode);
        Assert.Equal("God mode enabled.", fixture.LastMessage());

        Assert.True(await fixture.Dispatch("/god off"));
        Assert.False(fixture.Character.GodMode);
        Assert.Equal("God mode disabled.", fixture.LastMessage());
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.Player | AccountAccessLevel.PTR)]
    public async Task Hide_the_command_from_non_staff(AccountAccessLevel level)
    {
        var fixture = new Fixture(level);

        Assert.False(await fixture.Dispatch("/god on"));
        Assert.False(fixture.Character.GodMode);
        Assert.Empty(fixture.Messages());
    }

    [Theory]
    [InlineData("/god")]
    [InlineData("/god on extra")]
    [InlineData("/god maybe")]
    public async Task Reject_invalid_arguments_without_changing_state(string command)
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);

        Assert.True(await fixture.Dispatch(command));
        Assert.False(fixture.Character.GodMode);
        Assert.Equal("Usage: /god <on|off>", fixture.LastMessage());
    }

    [Fact]
    public async Task Report_repeated_state_without_changing_it()
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);

        await fixture.Dispatch("/god off");
        Assert.Equal("God mode is already disabled.", fixture.LastMessage());
        await fixture.Dispatch("/god ON");
        await fixture.Dispatch("/god on");
        Assert.True(fixture.Character.GodMode);
        Assert.Equal("God mode is already enabled.", fixture.LastMessage());
    }

    [Fact]
    public async Task Refuse_to_enable_a_dead_character()
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);
        fixture.Character.IsDead = true;

        Assert.True(await fixture.Dispatch("/god on"));
        Assert.False(fixture.Character.GodMode);
        Assert.Equal("Cannot enable god mode while dead.", fixture.LastMessage());
    }

    [Fact]
    public async Task Leave_a_new_character_out_of_god_mode()
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);
        await fixture.Dispatch("/god on");

        var other = new CharacterEntity(NullLoggerFactory.Instance,
            new Character { Id = 2u, Health = 100 }, new RegenConfiguration());

        Assert.False(other.GodMode);
    }

    private sealed class Fixture
    {
        private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
        private readonly List<NetworkPacket> _sent = [];
        private readonly CommandDispatcher _dispatcher;

        public Fixture(AccountAccessLevel level)
        {
            Character = new CharacterEntity(NullLoggerFactory.Instance,
                new Character { Id = 1u, Health = 100 }, new RegenConfiguration());
            _connection.Character.Returns(Character);
            _connection.AccessLevel.Returns(level);
            _connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
            _connection.When(c => c.Send(Arg.Any<NetworkPacket>()))
                .Do(call => _sent.Add(call.Arg<NetworkPacket>()));
            _dispatcher = new CommandDispatcher(
                [new GodModeCommand(NullLogger<GodModeCommand>.Instance)],
                NullLogger<CommandDispatcher>.Instance);
        }

        public CharacterEntity Character { get; }

        public Task<bool> Dispatch(string message) => _dispatcher.DispatchAsync(new WorldPacketContext<CChatMessagePacket>
        {
            Packet = new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow },
            Connection = _connection
        });

        public string LastMessage() => Assert.Single(Messages().TakeLast(1));

        public List<string> Messages() => _sent
            .Where(packet => packet.Header.Type == NetworkPacketType.SMSG_CHAT_MESSAGE)
            .Select(packet =>
            {
                using var stream = new MemoryStream(packet.Payload);
                return Serializer.Deserialize<SChatMessagePacket>(stream).Message;
            })
            .ToList();
    }
}
