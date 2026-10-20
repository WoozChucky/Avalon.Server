using System.Globalization;
using Avalon.Combat;
using Avalon.Common.Accounts;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Social;
using Avalon.World.Characters;
using Avalon.World.Chat;
using Avalon.World.Entities;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Chat;

public class SpeedCommandShould
{
    [Theory]
    [InlineData(AccountAccessLevel.GameMaster, "/speed")]
    [InlineData(AccountAccessLevel.Admin, "/speed 1")]
    public void Multiply_the_staff_characters_speed_through_refreshes_and_publish_changes(
        AccountAccessLevel level, string reset)
    {
        var fixture = new Fixture(level);
        fixture.Refresh(0);
        fixture.Flush();

        Assert.True(fixture.Dispatch("/speed 5"));
        Assert.Equal(20f, fixture.Character.GetMovementSpeed());
        Assert.Equal("Movement speed: 5x (20 m/s).", fixture.Messages().Last());
        fixture.Flush();
        fixture.Flush();

        // A refresh still applies the ordinary cap and slows, then the staff multiplier.
        fixture.Refresh(100);
        Assert.Equal(27f, fixture.Character.GetMovementSpeed(), precision: 4);
        fixture.Flush();
        fixture.Refresh(-100);
        Assert.Equal(10f, fixture.Character.GetMovementSpeed());
        fixture.Flush();

        Assert.True(fixture.Dispatch(reset));
        Assert.Equal(2f, fixture.Character.GetMovementSpeed());
        fixture.Flush();
        Assert.Equal(new[] { 4f, 20f, 27f, 10f, 2f }, fixture.Read<SCharacterStatsPacket>(
            NetworkPacketType.SMSG_CHARACTER_STATS).Select(p => p.MovementSpeed));
    }

    [Theory]
    [InlineData("2.5", 10f)]
    [InlineData("10", 40f)]
    public void Accept_decimal_multipliers_and_the_upper_bound(string argument, float expected)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-PT");
            var fixture = new Fixture(AccountAccessLevel.GameMaster);
            Assert.True(fixture.Dispatch("/speed " + argument));
            Assert.Equal(expected, fixture.Character.GetMovementSpeed());
            Assert.Contains(argument + "x", fixture.Messages().Last(), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.Player | AccountAccessLevel.PTR)]
    public void Hide_the_command_from_non_staff(AccountAccessLevel level)
    {
        var fixture = new Fixture(level);

        // ChatMessageHandler answers "Unknown command." when Dispatch returns false.
        Assert.False(fixture.Dispatch("/speed 5"));
        Assert.Equal(4f, fixture.Character.GetMovementSpeed());
        Assert.Empty(fixture.Messages());
    }

    [Theory]
    [InlineData("/speed 0")]
    [InlineData("/speed 0.5")]
    [InlineData("/speed 10.1")]
    [InlineData("/speed -1")]
    [InlineData("/speed +2")]
    [InlineData("/speed 1e1")]
    [InlineData("/speed NaN")]
    [InlineData("/speed Infinity")]
    [InlineData("/speed 999999999999999999999999999999999999999999")]
    [InlineData("/speed 2,5")]
    [InlineData("/speed fast")]
    [InlineData("/speed 2 extra")]
    public void Reject_invalid_arguments_without_losing_the_current_override(string command)
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);
        fixture.Dispatch("/speed 5");

        Assert.True(fixture.Dispatch(command));

        Assert.Equal(20f, fixture.Character.GetMovementSpeed());
        Assert.StartsWith("Usage: /speed", fixture.Messages().Last());
    }

    private sealed class Fixture
    {
        private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
        private readonly List<OutboundPacket> _sent = [];
        private readonly CommandDispatcher _dispatcher;

        public Fixture(AccountAccessLevel level)
        {
            Character = new CharacterEntity(NullLoggerFactory.Instance,
                new Character { Id = 1u, Health = 100 }, new RegenConfiguration());
            _connection.Character.Returns(Character);
            _connection.AccessLevel.Returns(level);
            _connection.When(c => c.Send(Arg.Any<OutboundPacket>()))
                .Do(call => _sent.Add(call.Arg<OutboundPacket>()));
            _dispatcher = new CommandDispatcher(
                [new SpeedCommand(NullLogger<SpeedCommand>.Instance)],
                NullLogger<CommandDispatcher>.Instance, TimeProvider.System);
        }

        public CharacterEntity Character { get; }

        public void Refresh(float speedPct) => Character.ApplyStats(
            new DerivedCharacterStats { MaxHealth = 100, MovementSpeedPct = speedPct },
            CurrentValues.KeepShare, TestCombat.Formula);

        public void Flush() => CharacterSheetFlusher.Flush(_connection, TestCombat.Formula);

        public bool Dispatch(string message) => _dispatcher.Dispatch(_connection,
            new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow });

        public IEnumerable<string> Messages() =>
            Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE).Select(p => p.Message);

        public IEnumerable<T> Read<T>(NetworkPacketType type) => _sent
            .Where(packet => packet.Header.Type == type)
            .Select(packet =>
            {
                using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
                return Serializer.Deserialize<T>(stream);
            });
    }
}
