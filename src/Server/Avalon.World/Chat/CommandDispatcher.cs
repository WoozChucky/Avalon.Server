using Avalon.Common.Accounts;
using Avalon.Network.Packets.Social;
using Avalon.World.Public;
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

    public bool Dispatch(IWorldConnection connection, CChatMessagePacket packet)
    {
        string[] parts = packet.Message.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0 || !_commands.TryGetValue(parts[0], out ICommand? command))
        {
            return false;
        }

        // Deliberately indistinguishable from an unknown command: the caller learns nothing about
        // which commands exist. A mask test, never ">=" — AccountAccessLevel is [Flags].
        if (!command.RequiredAccess.Allows(connection.AccessLevel))
        {
            return false;
        }

        var ctx = new CommandContext(connection, packet, failure => ReportFailure(connection, packet, command, failure));

        try
        {
            command.Execute(ctx, parts[1..]);
        }
        catch (Exception ex)
        {
            ReportFailure(connection, packet, command, ex);
        }

        // True even on failure: the command was found and ran, so "Unknown command." would be wrong.
        return true;
    }

    /// <summary>
    /// A command that threw, a task it handed to <see cref="CommandContext.Then{T}" /> that faulted or was
    /// cancelled, or a callback that threw (#443): logged at Error, and told to staff by type only.
    /// </summary>
    private void ReportFailure(IWorldConnection connection, CChatMessagePacket packet, ICommand command, Exception ex)
    {
        _logger.LogError(ex, "Command /{Command} failed for account {AccountId}", command.Name, connection.AccountId);

        if (SeesCommandFailures.Allows(connection.AccessLevel))
        {
            // Type name only — the message and stack stay in the log.
            connection.Send(SChatMessagePacket.System($"Command /{command.Name} failed: {ex.GetType().Name}.",
                packet.DateTime, connection.CryptoSession.Encrypt));
        }
    }
}
