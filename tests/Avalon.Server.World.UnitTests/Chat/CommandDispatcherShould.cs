using Avalon.Common.Accounts;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Chat;

public class CommandDispatcherShould
{
    [Fact]
    public void Run_a_staff_command_for_a_game_master()
    {
        ICommand command = Command("reload", AccessLevels.GameMaster);

        Assert.True(Dispatch(command, "/reload dialogue", AccountAccessLevel.GameMaster, out _));
        command.Received(1).Execute(Arg.Any<CommandContext>(), Arg.Is<string[]>(a => a.SequenceEqual(new[] { "dialogue" })));
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    public void Answer_a_forbidden_command_exactly_as_an_unknown_one(AccountAccessLevel actual)
    {
        ICommand command = Command("reload", AccessLevels.GameMaster);

        Assert.False(Dispatch(command, "/reload dialogue", actual, out _));
        command.DidNotReceiveWithAnyArgs().Execute(default!, default!);
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    public void Let_a_player_run_a_command_that_declares_no_access(AccountAccessLevel level) =>
        Assert.True(Dispatch(new Undeclared(), "/undeclared", level, out _));

    [Theory]
    [InlineData(AccountAccessLevel.GameMaster)]
    [InlineData(AccountAccessLevel.Admin)]
    public void Tell_staff_a_command_failed_and_why(AccountAccessLevel level)
    {
        ICommand command = Command("reload", AccessLevels.Player);
        command.When(c => c.Execute(Arg.Any<CommandContext>(), Arg.Any<string[]>()))
            .Do(_ => throw new InvalidOperationException("secret detail"));

        Assert.True(Dispatch(command, "/reload", level, out CommandConnection connection));
        Assert.Equal("Command /reload failed: InvalidOperationException.", Assert.Single(connection.Messages()));
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player)]
    [InlineData(AccountAccessLevel.Tournament)]
    [InlineData(AccountAccessLevel.PTR)]
    [InlineData(AccountAccessLevel.Console)]
    public void Say_nothing_to_anyone_else_when_a_command_fails(AccountAccessLevel level)
    {
        Assert.True(Dispatch(new Then(Task.FromException<int>(new InvalidOperationException("x"))), "/then", level,
            out CommandConnection connection));
        Assert.Empty(connection.Messages());
    }

    [Fact]
    public void Report_a_faulted_task_to_staff()
    {
        Dispatch(new Then(Task.FromException<int>(new TimeoutException("x"))), "/then", AccountAccessLevel.GameMaster,
            out CommandConnection connection);

        Assert.Equal("Command /then failed: TimeoutException.", Assert.Single(connection.Messages()));
    }

    [Fact]
    public void Report_a_cancelled_task_to_staff()
    {
        Dispatch(new Then(Task.FromCanceled<int>(new CancellationToken(canceled: true))), "/then",
            AccountAccessLevel.GameMaster, out CommandConnection connection);

        Assert.Equal("Command /then failed: TaskCanceledException.", Assert.Single(connection.Messages()));
    }

    [Fact]
    public void Report_a_throwing_callback_to_staff()
    {
        Dispatch(new Then(Task.FromResult(1), _ => throw new ArgumentException("x")), "/then",
            AccountAccessLevel.GameMaster, out CommandConnection connection);

        Assert.Equal("Command /then failed: ArgumentException.", Assert.Single(connection.Messages()));
    }

    [Fact]
    public void Run_the_callback_with_the_result()
    {
        int seen = 0;
        Dispatch(new Then(Task.FromResult(42), v => seen = v), "/then", AccountAccessLevel.Player, out _);

        Assert.Equal(42, seen);
    }

    [Fact]
    public void Log_every_failure_whoever_ran_it()
    {
        var logger = new CapturingLogger();
        var failure = new InvalidOperationException("x");
        var connection = new CommandConnection(AccountAccessLevel.Player);

        new CommandDispatcher([new Then(Task.FromException<int>(failure))], logger)
            .Dispatch(connection.Connection, new CChatMessagePacket { Message = "/then", DateTime = DateTime.UtcNow });

        Assert.Same(failure, Assert.Single(logger.Errors));
    }

    private static bool Dispatch(ICommand command, string message, AccountAccessLevel level, out CommandConnection connection)
    {
        connection = new CommandConnection(level);
        return new CommandDispatcher([command], new CapturingLogger())
            .Dispatch(connection.Connection, new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow });
    }

    private static ICommand Command(string name, AccountAccessLevel required)
    {
        ICommand command = Substitute.For<ICommand>();
        command.Name.Returns(name);
        command.Aliases.Returns([]);
        command.RequiredAccess.Returns(required);
        return command;
    }

    private sealed class Undeclared : ICommand
    {
        public string Name => "undeclared";
        public string[] Aliases => [];
        public void Execute(CommandContext ctx, string[] args) { }
    }

    private sealed class Then(Task<int> task, Action<int>? callback = null) : ICommand
    {
        public string Name => "then";
        public string[] Aliases => [];
        public void Execute(CommandContext ctx, string[] args) => ctx.Then(task, callback ?? (_ => { }));
    }

    private sealed class CapturingLogger : ILogger<CommandDispatcher>
    {
        public List<Exception?> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(exception);
        }
    }
}
