using Avalon.Network;
using Avalon.Network.Packets.Social;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    public async Task HandleChatMessagePacket(IRemoteSource source, CChatMessagePacket packet)
    {
        var msgPacket = SChatMessagePacket.Create(packet.ClientId, packet.Message, packet.DateTime);
        
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
                var player = _players.Values.FirstOrDefault(p => p.Id == playerName);
                if (player == null)
                {
                    _logger.LogInformation("Player {PlayerName} not found for group invite", playerName);
                    return;
                }
                await player.Connection.SendAsync(SGroupInvitePacket.Create(player.Id, packet.ClientId));
            }
        }
        else
        {
            await BroadcastToOthers(packet.ClientId, msgPacket);
        }
    }
    
    public Task HandleOpenChatPacket(IRemoteSource source, COpenChatPacket packet)
    {
        if (_players.TryGetValue(packet.ClientId, out var player))
        {
            player.Character.IsChatting = true;
        }
        return Task.CompletedTask;
    }

    public Task HandleCloseChatPacket(IRemoteSource source, CCloseChatPacket packet)
    {
        if (_players.TryGetValue(packet.ClientId, out var player))
        {
            player.Character.IsChatting = false;
        }
        return Task.CompletedTask;
    }
}
