using System.Drawing;
using Avalon.Network;
using Avalon.Network.Packets.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    public async Task HandleInteractPacket(IRemoteSource source, CInteractPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            LoggerExtensions.LogWarning(_logger, "Received interact packet from unknown account: {AccountId}", packet.AccountId);
            return;
        }

        if (!session.InGame)
        {
            LoggerExtensions.LogWarning(_logger, "Received interact packet from account that is not in game: {AccountId}", packet.AccountId);
            return;
        }
        
        var instance = _mapManager.GetInstance(session.Character!.Map, session.Character.Id);
        
        if (instance == null)
        {
            LoggerExtensions.LogWarning(_logger, "Received interact packet from account that is not in an instance: {AccountId}", packet.AccountId);
            return;
        }
        
        var playerInteractionArea = new Rectangle(packet.X, packet.Y, packet.Width, packet.Height);

        foreach (var (_, creature) in instance.Creatures)
        {
            if (creature.Bounds.IntersectsWith(playerInteractionArea))
            {
                LoggerExtensions.LogInformation(_logger, "Character {CharacterName} interacted with creature {CreatureName}", session.Character.Name, creature.Name);
                creature.Script?.OnCharacterInteraction(session.Character);
                return;
            }
        }

        foreach (var @event in instance.Events)
        {
            if (@event.Bounds.IntersectsWith(playerInteractionArea))
            {
                LoggerExtensions.LogInformation(_logger, "Character {CharacterName} interacted with event {EventName} ({EventClass})", session.Character.Name, @event.Name, @event.Class);
                return;
            }
        }
    }
}
