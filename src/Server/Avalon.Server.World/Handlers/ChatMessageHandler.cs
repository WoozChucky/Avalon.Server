using Avalon.Network.Packets.Social;
using Avalon.World;

namespace Avalon.Server.World.Handlers;

public class ChatMessageHandler : IWorldPacketHandler<CChatMessagePacket>
{
    public Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, CancellationToken token = default)
    {
        var message = ctx.Packet.Message;
        var dateTime = ctx.Packet.DateTime;

        /*
         *
        var message = packet.Message.Trim();

        if (message.StartsWith("/")) //TODO: Make this a command handler
        {
            var command = message.Substring(1);
            var args = command.Split(' ');
            var cmd = args[0].ToLower();
            var cmdArgs = args.Skip(1).ToArray();

            if (cmd is "inv" or "invite")
            {
                var playerName = cmdArgs[0];
                if (string.IsNullOrEmpty(playerName))
                {
                    _logger.LogInformation("Invalid player name for group invite");
                    return;
                }
                var invitedAccount = _sessionManager.GetSessions().Values.FirstOrDefault(p => p.InGame && p.Character?.Name == playerName);
                if (invitedAccount == null)
                {
                    _logger.LogInformation("Player {PlayerName} not found for group invite", playerName);
                    return;
                }
                
                if (invitedAccount.AccountId == session.AccountId)
                {
                    _logger.LogInformation("Player {PlayerName} tried to invite themselves to a group", playerName);
                    return;
                }
                
                await invitedAccount.SendAsync(SGroupInvitePacket.Create(invitedAccount.AccountId, session.AccountId, session.Character!.Id!.Value, session.Character.Name, invitedAccount.Encrypt));
            }
        }
        else
        {
            // Broadcast to all players

            var availableSessions = _sessionManager.GetSessions().Values
                .Where(p => p.InGame && p.AccountId != session.AccountId);
            
            var tasks = availableSessions.Select(s => s.SendAsync(
                SChatMessagePacket.Create(
                    packet.AccountId, 
                    session.Character!.Id!.Value,
                    session.Character.Name, 
                    packet.Message, 
                    packet.DateTime,
                    s.Encrypt))
            );
            
            await Task.WhenAll(tasks);
        }
        */

        return Task.CompletedTask;
    }
}
