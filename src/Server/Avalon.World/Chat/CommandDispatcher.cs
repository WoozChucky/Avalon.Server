using Avalon.Common.Accounts;
using Avalon.Network.Packets.Social;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Chat;

public sealed class CommandDispatcher : ICommandDispatcher
{
    // Who is told that a command failed, and why. Everyone else gets silence: an error line would
    // tell them the command exists and what inside it broke. Named flags, not AccessLevels.Admin —
    // that mask includes Console. A mask test, never ">=": AccountAccessLevel is [Flags].
    private const AccountAccessLevel SeesCommandFailures = AccountAccessLevel.GameMaster | AccountAccessLevel.Admin;

    private readonly Dictionary<string, ICommand> _commands;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(IEnumerable<ICommand> commands, ILogger<CommandDispatcher> logger)
    {
        _logger = logger;
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

        // Deliberately indistinguishable from an unknown command: the caller learns nothing about
        // which commands exist. A mask test, never ">=" — AccountAccessLevel is [Flags].
        if (!command.RequiredAccess.Allows(ctx.Connection.AccessLevel))
        {
            return false;
        }

        var args = parts[1..];

        try
        {
            await command.ExecuteAsync(ctx, args, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Caught here because nothing downstream will: the connection's continuation drain logs
            // a faulted task and drops its callback, so an escaping exception leaves the caller with
            // no reply at all (#443).
            _logger.LogError(ex, "Command /{Command} failed for account {AccountId}",
                command.Name, ctx.Connection.AccountId);

            if (SeesCommandFailures.Allows(ctx.Connection.AccessLevel))
            {
                // Type name only — the message and stack stay in the log.
                ctx.Connection.Send(SChatMessagePacket.Create(
                    0UL, 0UL, "System", $"Command /{command.Name} failed: {ex.GetType().Name}.",
                    ctx.Packet.DateTime, ctx.Connection.CryptoSession.Encrypt));
            }
        }

        // True even on failure: the command was found and ran, so "Unknown command." would be wrong.
        return true;
    }
}
