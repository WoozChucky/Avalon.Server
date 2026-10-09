using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// CMSG_CHARACTER_LEAVE (#663): back to character selection on the same world connection, with no new
/// login. The character leaves the world by the path every logout takes (<see cref="IWorld.LeaveWorldAsync" />:
/// out of its encounter, its conversation and its instance, and its logout save, which writes it
/// offline), and the connection is told <see cref="CharacterLeaveResult.Left" /> only once that save
/// has committed.
/// </summary>
/// <remarks>
/// <para>
/// Accepted by the session filter alone, whatever the connection holds, so it runs in the session pass,
/// before any instance ticks, and in order: in-map packets queued before it are handled by the map pass
/// first, and those queued behind it are dropped once the character is gone
/// (<c>WorldConnection.ProcessQueue</c>).
/// </para>
/// <para>
/// From the moment the leave starts, the connection holds no character and
/// <see cref="IWorldConnection.LeaveInProgress" /> is set, so the list, select, create and delete
/// handlers refuse it as they refuse a select in progress; a second leave is answered
/// <see cref="CharacterLeaveResult.AlreadyLeaving" /> and saves nothing. A save that fails, or a leave
/// that throws, is never answered: the connection is closed with
/// <see cref="DisconnectReason.CharacterSaveFailed" />, since the character's last state is not known to
/// be written. A connection that drops or is kicked while leaving is not answered either. A select of
/// the same character, on this or any connection, waits for the logout save anyway
/// (<c>ICharacterSaver.WhenIdle</c>), so an early reselect cannot read what the save is still writing.
/// </para>
/// </remarks>
[PacketHandler(NetworkPacketType.CMSG_CHARACTER_LEAVE)]
public class CharacterLeaveHandler(ILogger<CharacterLeaveHandler> logger, IWorld world)
    : WorldPacketHandler<CCharacterLeavePacket>
{
    public const string SaveFailedMessage = "Your character could not be saved. Please log in again.";

    public override void Execute(IWorldConnection connection, CCharacterLeavePacket packet)
    {
        if (connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to leave a character without being authenticated");
            connection.Close();
            return;
        }

        // Closing, or kicked: its close despawns the character, and there is nobody left to answer.
        if (connection.IsClosing)
            return;

        if (connection is not ICharacterLeaveControl control)
        {
            logger.LogError("Connection of account {AccountId} cannot leave a character", connection.AccountId);
            return;
        }

        if (connection.LeaveInProgress)
        {
            Answer(connection, CharacterLeaveResult.AlreadyLeaving);
            return;
        }

        if (connection.SelectInProgress || connection.PendingSpawn != null)
        {
            Answer(connection, CharacterLeaveResult.Selecting);
            return;
        }

        if (connection.Character is not { } character)
        {
            Answer(connection, CharacterLeaveResult.NoCharacter);
            return;
        }

        _ = control.TryBeginLeave();
        control.ResetCharacterState();

        logger.LogInformation("Character {CharacterName} of account {AccountId} is leaving for character selection",
            character.Name, connection.AccountId);

        // Runs on the tick up to the save, which it queues before its first await, and clears
        // connection.Character before it returns.
        connection.EnqueueContinuation(LeaveAsync(connection, character),
            committed => OnLeft(connection, control, character, committed));
    }

    /// <summary>
    /// Never faults: a continuation that faulted would be dropped and leave the connection leaving for
    /// good. <see cref="IWorld.LeaveWorldAsync" /> releases the character on the tick before its first
    /// await; one that throws before that leaves it held, and the close that answers a failed leave
    /// despawns it.
    /// </summary>
    private async Task<bool> LeaveAsync(IWorldConnection connection, ICharacter character)
    {
        try
        {
            return await world.LeaveWorldAsync(connection).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Character {CharacterId} failed to leave the world", character.Guid);
            return false;
        }
    }

    private void OnLeft(IWorldConnection connection, ICharacterLeaveControl control, ICharacter character,
        bool committed)
    {
        control.EndLeave();

        if (!connection.IsConnected || connection.IsClosing)
            return;

        if (!committed)
        {
            logger.LogError(
                "The logout save of character {CharacterId} failed while leaving for character selection; closing the connection of account {AccountId}",
                character.Guid, connection.AccountId);
#pragma warning disable MA0045 // a tick-thread callback; the close finishes on its own
            GracefulShutdownHelper.NotifyAndClose(connection, SaveFailedMessage, DisconnectReason.CharacterSaveFailed,
                logger);
#pragma warning restore MA0045
            return;
        }

        Answer(connection, CharacterLeaveResult.Left);
    }

    private static void Answer(IWorldConnection connection, CharacterLeaveResult result) =>
        connection.Send(SCharacterLeaveResultPacket.Create(result, connection.CryptoSession.Encryptor));
}
