using Avalon.Network;
using Avalon.Network.Packets.Social;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    
    public async Task HandleGroupInviteResultPacket(IRemoteSource source, CGroupInviteResultPacket packet)
    {
        var invitedSession = _sessionManager.GetSession(packet.AccountId);
        if (invitedSession == null)
        {
            _logger.LogInformation("Player {PlayerName} not found for group invite result", packet.AccountId);
            return;
        }
        
        var inviterSession = _sessionManager.GetSession(packet.InviterAccountId);
        if (inviterSession == null)
        {
            _logger.LogInformation("Player {PlayerName} not found for group invite result", packet.InviterAccountId);
            return;
        }

        if (!packet.Accepted)
        {
            _logger.LogInformation("Player {PlayerName} declined group invite from {InvitedByPlayerName}", packet.AccountId, packet.InviterAccountId);
            //await inviterSession.SendAsync(SGroupResultPacket.Create(inviterSession.AccountId, invitedSession.AccountId, false));
            return;
        }
        
        _logger.LogInformation("Player {PlayerName} accepted group invite from {InvitedByPlayerName}", packet.AccountId, packet.InviterAccountId);
        
        // TODO: Check if player is already in a group
        // TODO: Check if player is already in this group
        
        // Create a new group and add both players to it
        inviterSession.Party.Active = true;
        inviterSession.Party.Members.Add(invitedSession.Character.Id);
        inviterSession.Party.Leader = true;
        
        invitedSession.Party.Active = true;
        invitedSession.Party.Members.Add(inviterSession.Character.Id);
        invitedSession.Party.Leader = false;
        
        // Send group result packets to both players
        //await invitedSession.SendAsync(SGroupResultPacket.Create(invitedSession.AccountId, inviterSession.AccountId, true));
        //await inviterSession.SendAsync(SGroupResultPacket.Create(inviterSession.AccountId, invitedSession.AccountId, true));
    }
}
