using Avalon.Common.Accounts;
using Avalon.Network.Packets.Social;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Public;
using NSubstitute;
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

    [Fact]
    public async Task Let_A_Player_Run_A_Command_That_Declares_No_Access()
    {
        // Existing commands such as GroupInviteCommand declare nothing; the default must keep them
        // runnable by ordinary players.
        ICommand command = new UndeclaredCommand();

        Assert.True(await Dispatch(command, "/undeclared", AccountAccessLevel.Player));
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

    private static Task<bool> Dispatch(ICommand command, string message, AccountAccessLevel level)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.AccessLevel.Returns(level);

        var ctx = new WorldPacketContext<CChatMessagePacket>
        {
            Packet = new CChatMessagePacket { Message = message },
            Connection = connection
        };

        return new CommandDispatcher([command]).DispatchAsync(ctx);
    }

    private sealed class UndeclaredCommand : ICommand
    {
        public string Name => "undeclared";
        public string[] Aliases => [];
        public Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args,
            CancellationToken token = default) => Task.CompletedTask;
    }
}
