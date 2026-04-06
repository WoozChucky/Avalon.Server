using Avalon.Network.Packets.Social;

namespace Avalon.World.Chat;

public sealed class GroupInviteCommand : ICommand
{
    public string Name => "invite";
    public string[] Aliases => ["inv"];

    public Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args, CancellationToken token = default)
    {
        /*
        if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
        {
            // TODO: send "Specify a player name" error response
            return Task.CompletedTask;
        }

        var playerName = args[0];

        var invitedConnection = server.Connections
            .FirstOrDefault(c => c.InGame && c.Character?.Name == playerName);

        if (invitedConnection == null)
        {
            // TODO: send "Player not found" error response
            return Task.CompletedTask;
        }

        if (invitedConnection.AccountId == ctx.Connection.AccountId)
        {
            // TODO: send "Cannot invite yourself" error response
            return Task.CompletedTask;
        }

        invitedConnection.Send(SGroupInvitePacket.Create(
            (ulong)(long)invitedConnection.AccountId!,
            (ulong)(long)ctx.Connection.AccountId!,
            ctx.Connection.Character!.Guid.Id,
            ctx.Connection.Character.Name,
            invitedConnection.CryptoSession.Encrypt));
        */

        return Task.CompletedTask;
    }
}
