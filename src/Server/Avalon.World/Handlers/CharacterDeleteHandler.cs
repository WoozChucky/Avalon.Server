using Avalon.World.Public;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_DELETE)]
public class CharacterDeletetHandler(
    ILogger<CharacterDeletetHandler> logger,
    ICharacterRepository characterRepository,
    IWorldServer? worldServer = null) : WorldPacketHandler<CCharacterDeletePacket>
{
    public override void Execute(IWorldConnection connection, CCharacterDeletePacket packet)
    {
        if (connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to delete a character without being authenticated");
            connection.Close();
            return;
        }

        // A select that has not finished counts as selected. SelectInProgress is the span
        // where the entity is being built and both of the other two are still null; a delete
        // landing there deletes the character being spawned. A leave that has not finished counts
        // too (#663): its logout save is still writing, and the client must wait for its answer.
        if (connection.Character != null || connection.PendingSpawn != null ||
            connection.SelectInProgress || connection.LeaveInProgress)
        {
            logger.LogWarning("Connection tried to delete a character while already having a character selected");
            connection.Close();
            return;
        }

        if (connection.GameplayAuthority is not { } authority || authority.AccountId != connection.AccountId || connection.IsClosing)
        { connection.Close(); return; }
        var work = Task.Run(async () =>
        {
            try { return await characterRepository.DeleteForGameplayAsync(authority, packet.CharacterId, CancellationToken.None); }
            catch (Exception error)
            {
                logger.LogWarning(error, "Character delete failed for account {AccountId}", authority.AccountId);
                return false;
            }
        });
        connection.EnqueueContinuation(work, deleted =>
        {
            if (!connection.IsConnected || connection.IsClosing) return;
            connection.Send(SCharacterDeletedPacket.Create(deleted ? SCharacterDeletedResult.Success : SCharacterDeletedResult.InternalError, connection.CryptoSession.Encrypt));
            if (deleted) ForgetIgnored(packet.CharacterId);
        });

    }

    /// <summary>
    /// #723: the database cascades the deleted character's ignore rows; every character that already holds a loaded
    /// list and ignored it takes it off too (on the tick, in memory) and is sent the new list: those in the world and
    /// those selected and waiting on their load report (the pending spawn). A select whose ignore rows were read
    /// before the delete committed but that applies them only afterwards keeps the entry until its next select, which
    /// corrects it; the entry is harmless meanwhile, since the character can never speak again. Contained: a throw
    /// costs the lists, never the delete.
    /// </summary>
    private void ForgetIgnored(uint deletedId)
    {
        if (worldServer is null)
            return;

        try
        {
            foreach (IWorldConnection other in worldServer.Connections)
            {
                CharacterEntity? entity = other.Character as CharacterEntity ?? other.PendingSpawn?.Character as CharacterEntity;
                if (entity is not null && entity.Ignores.Remove(deletedId))
                    other.Send(entity.Ignores.ToPacket(other.CryptoSession.Encrypt));
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Taking deleted character {CharacterId} off the online ignore lists failed", deletedId);
        }
    }
}

