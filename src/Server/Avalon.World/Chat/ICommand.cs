using Avalon.Common.Accounts;
using Avalon.Network.Packets.Social;

namespace Avalon.World.Chat;

public interface ICommand
{
    string Name { get; }
    string[] Aliases { get; }

    Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args, CancellationToken token = default);

    /// <summary>
    /// Who may run this command. Defaults to any logged-in player, so a command that declares
    /// nothing stays runnable by everyone. A caller without this access is told "Unknown command."
    /// </summary>
    AccountAccessLevel RequiredAccess => AccessLevels.Player;
}
