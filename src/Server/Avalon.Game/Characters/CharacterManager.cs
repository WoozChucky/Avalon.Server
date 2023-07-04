using System.Numerics;
using Avalon.Database.Characters;
using Avalon.Network;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    public async Task HandleCharacterSelectedPacket(IRemoteSource source, CCharacterSelectedPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }

        if (session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is already in game", packet.AccountId);
            return;
        }

        var character = await _databaseManager.Characters.Character.QueryByIdAndAccountAsync(packet.CharacterId, packet.AccountId);
        
        if (character == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for account {AccountId}", packet.CharacterId, packet.AccountId);
            return;
        }

        character.Movement = new CharacterMovement
        {
            Position = new Vector2(character.PositionX, character.PositionY),
            Velocity = Vector2.Zero
        };
        
        session.Character = character;
        
        _logger.LogInformation("Character {CharacterId} logged in for account {AccountId}", character.Name, packet.AccountId);
        
        var sessions = _connectionManager.GetSessions();
        
        // Send to everyone else, that this player is connected
        await BroadcastToOthers(session.AccountId, SPlayerConnectedPacket.Create(session.AccountId, session.Character.Id, session.Character.Name), true);
        
        // Send to this player, that everyone else is connected
        foreach (var otherSession in sessions.Values)
        {
            if (otherSession.AccountId == session.AccountId || !otherSession.InGame)
            {
                continue;
            }
            await session.SendAsync(SPlayerConnectedPacket.Create(otherSession.AccountId, otherSession.Character!.Id, otherSession.Character.Name));
        }
    }
}
