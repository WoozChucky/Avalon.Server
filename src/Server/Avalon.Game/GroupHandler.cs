using Avalon.Network;
using Avalon.Network.Packets.Social;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    
    public async Task HandleGroupInviteResultPacket(IRemoteSource source, CGroupInviteResultPacket packet)
    {
        var invitedPlayer = _players.Values.FirstOrDefault(p => p.Id == packet.ClientId);
        if (invitedPlayer == null)
        {
            _logger.LogInformation("Player {PlayerName} not found for group invite result", packet.ClientId);
            return;
        }
        
        var invitedByPlayer = _players.Values.FirstOrDefault(p => p.Id == packet.InvitedById);
        if (invitedByPlayer == null)
        {
            _logger.LogInformation("Player {PlayerName} not found for group invite result", packet.InvitedById);
            return;
        }

        if (!packet.Accepted)
        {
            _logger.LogInformation("Player {PlayerName} declined group invite from {InvitedByPlayerName}", packet.ClientId, packet.InvitedById);
            await invitedByPlayer.Connection.SendAsync(SGroupResultPacket.Create(invitedByPlayer.Id, invitedPlayer.Id, false));
            return;
        }
        
        _logger.LogInformation("Player {PlayerName} accepted group invite from {InvitedByPlayerName}", packet.ClientId, packet.InvitedById);
        
        // TODO: Check if player is already in a group
        // TODO: Check if player is already in this group
        
        // Create a new group and add both players to it
        invitedByPlayer.Party.Active = true;
        invitedByPlayer.Party.Members.Add(invitedPlayer.Id);
        invitedByPlayer.Party.Leader = true;
        
        invitedPlayer.Party.Active = true;
        invitedPlayer.Party.Members.Add(invitedByPlayer.Id);
        invitedPlayer.Party.Leader = false;
        
        // Send group result packets to both players
        await invitedPlayer.Connection.SendAsync(SGroupResultPacket.Create(invitedPlayer.Id, invitedByPlayer.Id, true));
        await invitedByPlayer.Connection.SendAsync(SGroupResultPacket.Create(invitedByPlayer.Id, invitedPlayer.Id, true));
    }
}
