using System.Diagnostics;
using Avalon.Common.Telemetry;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Characters;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
///     The client reporting that it has composed the map it was sent at character-select. This is
///     what releases the readiness barrier and puts the character in its instance.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_CHARACTER_LOADED)]
public class CharacterLoadedHandler(
    ILogger<CharacterLoadedHandler> logger,
    IWorld world) : WorldPacketHandler<CCharacterLoadedPacket>
{
    public override void Execute(IWorldConnection connection, CCharacterLoadedPacket packet)
    {
        // Answered before the activity starts and at debug. The session filter takes this opcode
        // whatever the character state and nothing rate-limits it, so a client that sends it every
        // packet costs a span and a log line each, 150 times a tick.
        if (connection.PendingSpawn is not { } pending)
        {
            // A duplicate report, one that lost the race to the expiring barrier, or a client
            // that sent it without selecting. None of those is a protocol break.
            logger.LogDebug("Account {AccountId} reported a character loaded with no spawn pending",
                connection.AccountId);
            return;
        }

        using Activity? activity =
            DiagnosticsConfig.World.Source.StartActivity(nameof(CharacterLoadedHandler), ActivityKind.Server);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);

        string characterName = pending.Character.Name;
        activity?.SetTag("CharacterName", characterName);

        if (!CharacterReadinessBarrier.Release(connection, world, logger))
        {
            activity?.AddEvent(new ActivityEvent("SpawnFailed"));
            return;
        }

        logger.LogInformation("Character {CharacterName} entered the world for account {AccountId} at {Position}",
            characterName, connection.AccountId, connection.Character!.Position);
    }
}
