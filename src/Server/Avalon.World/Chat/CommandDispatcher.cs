using Avalon.Network.Packets.Social;

namespace Avalon.World.Chat;

public sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly Dictionary<string, ICommand> _commands;

    public CommandDispatcher(IEnumerable<ICommand> commands)
    {
        _commands = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            _commands[command.Name] = command;

            foreach (var alias in command.Aliases)
            {
                _commands[alias] = command;
            }
        }
    }

    public async Task<bool> DispatchAsync(WorldPacketContext<CChatMessagePacket> ctx, CancellationToken token = default)
    {
        var rawCommand = ctx.Packet.Message.TrimStart('/');
        var parts = rawCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return false;
        }

        if (!_commands.TryGetValue(parts[0], out var command))
        {
            return false;
        }

        var args = parts[1..];
        await command.ExecuteAsync(ctx, args, token);
        return true;
    }
}
