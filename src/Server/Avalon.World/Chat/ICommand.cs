using Avalon.Common.Accounts;

namespace Avalon.World.Chat;

public interface ICommand
{
    string Name { get; }
    string[] Aliases { get; }

    /// <summary>
    /// Runs on the tick, to completion, and cannot await. Asynchronous work goes through
    /// <see cref="CommandContext.Then{T}" />. <c>CommandsNeverBlockTheTickShould</c> fails any command with
    /// an async method or a blocking wait on a task.
    /// </summary>
    void Execute(CommandContext ctx, string[] args);

    /// <summary>
    /// Who may run this command. Defaults to any logged-in player, so a command that declares
    /// nothing stays runnable by everyone. A caller without this access is told "Unknown command."
    /// </summary>
    AccountAccessLevel RequiredAccess => AccessLevels.Player;
}
