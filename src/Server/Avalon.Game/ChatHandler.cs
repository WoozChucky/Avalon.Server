using Avalon.Network;
using Avalon.Network.Packets.Social;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    public async Task HandleChatMessagePacket(IRemoteSource source, CChatMessagePacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogInformation("Invalid account {AccountId} for group invite", packet.AccountId);
            return;
        }
        
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
                var invitedAccount = _connectionManager.GetSessions().Values.FirstOrDefault(p => p.InGame && p.Character.Name == playerName);
                if (invitedAccount == null)
                {
                    _logger.LogInformation("Player {PlayerName} not found for group invite", playerName);
                    return;
                }
                
                /*
                if (invitedAccount.AccountId == session.AccountId)
                {
                    _logger.LogInformation("Player {PlayerName} tried to invite themselves to a group", playerName);
                    return;
                }
                */
                
                await invitedAccount.SendAsync(SGroupInvitePacket.Create(invitedAccount.AccountId, session.AccountId, session.Character.Id, session.Character.Name));
            }
        }
        else
        {
            var msgPacket = SChatMessagePacket.Create(packet.AccountId, session.Character.Id, session.Character.Name, packet.Message, packet.DateTime);
            
            await BroadcastToOthers(packet.AccountId, msgPacket, true);
        }
    }
    
    public Task HandleOpenChatPacket(IRemoteSource source, COpenChatPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogInformation("Invalid account {AccountId} for chat", packet.AccountId);
            return Task.CompletedTask;
        }
        session.Character.IsChatting = true;
        return Task.CompletedTask;
    }

    public Task HandleCloseChatPacket(IRemoteSource source, CCloseChatPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogInformation("Invalid account {AccountId} for chat", packet.AccountId);
            return Task.CompletedTask;
        }
        session.Character.IsChatting = false;
        return Task.CompletedTask;
    }
}
