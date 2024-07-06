using Avalon.Network;
using Avalon.Network.Packets.Social;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    public async Task HandleChatMessagePacket(IRemoteSource source, CChatMessagePacket packet)
    {
        
    }
    
    public Task HandleOpenChatPacket(IRemoteSource source, COpenChatPacket packet)
    {
        var session = _sessionManager.GetSession(packet.AccountId);
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
        var session = _sessionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogInformation("Invalid account {AccountId} for chat", packet.AccountId);
            return Task.CompletedTask;
        }
        session.Character.IsChatting = false;
        return Task.CompletedTask;
    }
}
