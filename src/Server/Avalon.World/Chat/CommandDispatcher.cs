using Avalon.Common.Accounts;
using Avalon.Network.Packets.Serialization;
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

    // The commands by name and alias, whatever the case, looked up by a slice of the message: the command word is
    // never made a string (#883).
    private readonly Dictionary<string, ICommand>.AlternateLookup<ReadOnlySpan<char>> _commands;
    private readonly ILogger<CommandDispatcher> _logger;

    /// <param name="time">The clock every line a command sends is stamped with (#944).</param>
    public CommandDispatcher(IEnumerable<ICommand> commands, ILogger<CommandDispatcher> logger, TimeProvider time)
    {
        _logger = logger;
        Time = time;
        var byName = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);

        foreach (ICommand command in commands)
        {
            byName[command.Name] = command;

            foreach (string alias in command.Aliases)
            {
                byName[alias] = command;
            }
        }

        _commands = byName.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>The clock a command's lines are stamped with: <see cref="CommandContext.Now" />.</summary>
    internal TimeProvider Time { get; }

    /// <summary>
    /// Runs the command the message names, with the words after it as its arguments. The message is read as it always
    /// was: every leading slash is dropped, then it is cut at spaces (the space character only) and the empty pieces
    /// are left out; the first piece is the command word, whatever its case, and the rest are the arguments, as typed.
    /// </summary>
    /// <remarks>
    /// A message that names no command, or one the caller may not run, allocates nothing: a client can send those as
    /// fast as its connection is read. A command that runs is given a context and its arguments, and nothing else is
    /// made; one that reads only the message's own text (<see cref="ICommand.ReadsRawText" />) is given no arguments. Before #883 every message was trimmed into a new string and split into an array of strings before the
    /// command was looked up, and the failure report was a closure made on entry, whether a command ran or not.
    /// </remarks>
    public bool Dispatch(IWorldConnection connection, CChatMessagePacket packet)
    {
        ReadOnlySpan<char> text = packet.Message.AsSpan().TrimStart('/').TrimStart(' ');
        int end = text.IndexOf(' ');
        ReadOnlySpan<char> word = end < 0 ? text : text[..end];

        if (word.IsEmpty || !_commands.TryGetValue(word, out ICommand? command))
        {
            return false;
        }

        // Deliberately indistinguishable from an unknown command: the caller learns nothing about
        // which commands exist. A mask test, never ">=" — AccountAccessLevel is [Flags].
        if (!command.RequiredAccess.Allows(connection.AccessLevel))
        {
            return false;
        }

        var ctx = new CommandContext(connection, packet, this, command);

        try
        {
            // A command that reads only the message's own text is handed no arguments: it would not read them (#973).
            command.Execute(ctx, command.ReadsRawText ? [] : Arguments(text[word.Length..]));
        }
        catch (Exception ex)
        {
            ReportFailure(connection, packet, command, ex);
        }

        // True even on failure: the command was found and ran, so "Unknown command." would be wrong.
        return true;
    }

    /// <summary>
    /// The pieces between spaces, the empty ones left out: what splitting the whole message gave after its first
    /// piece. Each argument is a string of its own, since a command may keep them.
    /// </summary>
    private static string[] Arguments(ReadOnlySpan<char> rest)
    {
        int count = 0;
        foreach (Range piece in rest.Split(' '))
        {
            if (!rest[piece].IsEmpty)
                count++;
        }

        if (count == 0)
            return [];

        string[] arguments = new string[count];
        int next = 0;
        foreach (Range piece in rest.Split(' '))
        {
            if (!rest[piece].IsEmpty)
                arguments[next++] = rest[piece].ToString();
        }

        return arguments;
    }

    /// <summary>
    /// A command that threw, a task it handed to <see cref="CommandContext.Then{T}" /> that faulted or was
    /// cancelled, or a callback that threw (#443): logged at Error, and told to staff by type only.
    /// </summary>
    internal void ReportFailure(IWorldConnection connection, CChatMessagePacket packet, ICommand command, Exception ex)
    {
        _logger.LogError(ex, "Command /{Command} failed for account {AccountId}", command.Name, connection.AccountId);

        if (SeesCommandFailures.Allows(connection.AccessLevel))
        {
            // Type name only — the message and stack stay in the log.
            connection.Send(SChatMessagePacket.System($"Command /{command.Name} failed: {ex.GetType().Name}.",
                Time.GetUtcNow().UtcDateTime, PacketEncoder.Shared));
        }
    }
}
