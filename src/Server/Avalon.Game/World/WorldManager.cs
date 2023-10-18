using System.Drawing;
using Avalon.Network;
using Avalon.Network.Packets.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    public async Task HandleInteractPacket(IRemoteSource source, CInteractPacket packet)
    {
        var session = _sessionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogWarning("Received interact packet from unknown account: {AccountId}", packet.AccountId);
            return;
        }

        if (!session.InGame)
        {
            _logger.LogWarning("Received interact packet from account that is not in game: {AccountId}", packet.AccountId);
            return;
        }
        
        if (!session.InMap)
        {
            _logger.LogWarning("Received interact packet from account that is not in a map: {AccountId}", packet.AccountId);
            return;
        }
        
        var instance = _mapManager.GetInstance(session.Character!.Map, session);
        
        if (instance == null)
        {
            _logger.LogWarning("Received interact packet from account that is not in an instance: {AccountId}", packet.AccountId);
            return;
        }
        
        var playerInteractionArea = new Rectangle(packet.X, packet.Y, packet.Width, packet.Height);

        foreach (var (_, creature) in instance.Creatures)
        {
            if (creature.Bounds.IntersectsWith(playerInteractionArea))
            {
                _logger.LogInformation("Character {CharacterName} interacted with creature {CreatureName}", session.Character.Name, creature.Name);
                creature.Script?.OnCharacterInteraction(session.Character);
                return;
            }
        }

        foreach (var @event in instance.Events)
        {
            if (@event.Bounds.IntersectsWith(playerInteractionArea))
            {
                _logger.LogInformation("Character {CharacterName} interacted with event {EventName} ({EventClass})", session.Character.Name, @event.Name, @event.Class);
                return;
            }
        }
    }
}
