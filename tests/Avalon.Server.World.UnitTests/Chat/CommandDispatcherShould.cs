using System.IO;
using Avalon.Common.Accounts;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ProtoBuf;
using Xunit;

namespace Avalon.Server.World.UnitTests.Chat;

public class CommandDispatcherShould
{
    [Fact]
    public async Task Run_A_Staff_Command_For_A_Game_Master()
    {
        ICommand command = Command("reload", AccessLevels.GameMaster);

        bool dispatched = await Dispatch(command, "/reload dialogue", AccountAccessLevel.GameMaster);

        Assert.True(dispatched);
        await command.Received(1).ExecuteAsync(
            Arg.Any<WorldPacketContext<CChatMessagePacket>>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    public async Task Answer_A_Forbidden_Command_Exactly_As_An_Unknown_One(AccountAccessLevel actual)
    {
        // Returning false is what makes ChatMessageHandler reply "Unknown command." — the caller
        // learns nothing about which commands exist, and cannot tell forbidden from absent.
        ICommand command = Command("reload", AccessLevels.GameMaster);

        bool dispatched = await Dispatch(command, "/reload dialogue", actual);

        Assert.False(dispatched);
        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(default, default!, default);
    }

    /// <summary>
    /// Existing commands such as GroupInviteCommand declare nothing; the default must keep them
    /// runnable by ordinary players — and Tournament and PTR accounts are ordinary players (#447).
    /// </summary>
    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    public async Task Let_A_Player_Run_A_Command_That_Declares_No_Access(AccountAccessLevel level)
    {
        ICommand command = new UndeclaredCommand();

        Assert.True(await Dispatch(command, "/undeclared", level));
    }

    /// <summary>
    /// Issue #443. A command that throws used to fault the dispatch task, and the connection's
    /// continuation drain logs a faulted task and drops its callback — so the caller got no reply at
    /// all. Staff get told the command failed and why, by exception type only: no message, no stack.
    /// </summary>
    [Theory]
    [InlineData(AccountAccessLevel.GameMaster)]
    [InlineData(AccountAccessLevel.Admin)]
    public async Task Tell_Staff_A_Command_Failed_And_Why(AccountAccessLevel level)
    {
        ICommand command = Throwing("reload", new InvalidOperationException("secret detail"), required: level);
        var fixture = new Fixture(level);

        bool dispatched = await fixture.Dispatch(command, "/reload dialogue");

        Assert.True(dispatched);
        Assert.Equal("Command /reload failed: InvalidOperationException.", Assert.Single(fixture.SentMessages()));
    }

    /// <summary>
    /// Everyone else gets silence, on purpose: an error line would tell them the command exists and
    /// what inside it broke. Tournament and PTR are listed because they are the two an ordinal
    /// "at least GameMaster" test would wrongly let through; Console because it is not a GM or an
    /// Admin either. Returning true is what keeps ChatMessageHandler from saying "Unknown command."
    /// — the command was found and ran.
    /// </summary>
    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.Console)]
    public async Task Say_Nothing_To_Anyone_Else_When_A_Command_Fails(AccountAccessLevel level)
    {
        ICommand command = Throwing("undeclared", new InvalidOperationException("secret detail"));
        var fixture = new Fixture(level);

        bool dispatched = await fixture.Dispatch(command, "/undeclared");

        Assert.True(dispatched);
        Assert.Empty(fixture.SentMessages());
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.GameMaster)]
    public async Task Log_A_Failed_Command_Whoever_Ran_It(AccountAccessLevel level)
    {
        var failure = new InvalidOperationException("secret detail");
        var fixture = new Fixture(level);

        await fixture.Dispatch(Throwing("undeclared", failure), "/undeclared");

        Assert.Same(failure, Assert.Single(fixture.Logger.Errors));
    }

    /// <summary>
    /// A command can also throw before its first await — synchronously out of ExecuteAsync rather
    /// than through the returned task. Both must land in the same place.
    /// </summary>
    [Fact]
    public async Task Handle_A_Command_That_Throws_Before_Returning_A_Task()
    {
        ICommand command = Command("reload", AccessLevels.Player);
        command.ExecuteAsync(default, default!, default)
            .ReturnsForAnyArgs<Task>(_ => throw new ArgumentException("bad"));
        var fixture = new Fixture(AccountAccessLevel.GameMaster);

        bool dispatched = await fixture.Dispatch(command, "/reload");

        Assert.True(dispatched);
        Assert.Equal("Command /reload failed: ArgumentException.", Assert.Single(fixture.SentMessages()));
    }

    private static ICommand Command(string name, AccountAccessLevel required)
    {
        ICommand command = Substitute.For<ICommand>();
        command.Name.Returns(name);
        command.Aliases.Returns([]);
        command.RequiredAccess.Returns(required);
        command.ExecuteAsync(default, default!, default).ReturnsForAnyArgs(Task.CompletedTask);
        return command;
    }

    private static ICommand Throwing(string name, Exception failure,
        AccountAccessLevel required = AccessLevels.Player)
    {
        ICommand command = Command(name, required);
        command.ExecuteAsync(default, default!, default).ReturnsForAnyArgs(Task.FromException(failure));
        return command;
    }

    private static Task<bool> Dispatch(ICommand command, string message, AccountAccessLevel level)
        => new Fixture(level).Dispatch(command, message);

    private sealed class Fixture
    {
        private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
        private readonly List<NetworkPacket> _sent = [];

        public Fixture(AccountAccessLevel level)
        {
            _connection.AccessLevel.Returns(level);
            _connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
            _connection.When(c => c.Send(Arg.Any<NetworkPacket>()))
                .Do(ci => _sent.Add(ci.Arg<NetworkPacket>()));
        }

        public CapturingLogger Logger { get; } = new();

        public Task<bool> Dispatch(ICommand command, string message)
        {
            var ctx = new WorldPacketContext<CChatMessagePacket>
            {
                Packet = new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow },
                Connection = _connection
            };

            return new CommandDispatcher([command], Logger).DispatchAsync(ctx).WaitAsync(TimeSpan.FromSeconds(5));
        }

        /// <summary>FakeAvalonCryptoSession.Encrypt is a pass-through, as in ReloadCommandShould.</summary>
        public List<string> SentMessages() => _sent
            .Where(p => p.Header.Type == NetworkPacketType.SMSG_CHAT_MESSAGE)
            .Select(p =>
            {
                using var stream = new MemoryStream(p.Payload);
                return Serializer.Deserialize<SChatMessagePacket>(stream).Message;
            })
            .ToList();
    }

    private sealed class CapturingLogger : ILogger<CommandDispatcher>
    {
        public List<Exception?> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add(exception);
            }
        }
    }

    private sealed class UndeclaredCommand : ICommand
    {
        public string Name => "undeclared";
        public string[] Aliases => [];
        public Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args,
            CancellationToken token = default) => Task.CompletedTask;
    }
}
