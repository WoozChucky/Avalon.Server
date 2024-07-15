using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_DELETE)]
public class CharacterDeletetHandler(
    ILogger<CharacterDeletetHandler> logger,
    ICharacterRepository characterRepository) : WorldPacketHandler<CCharacterDeletePacket>
{
    public override void Execute(WorldConnection connection, CCharacterDeletePacket packet)
    {
        if (connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to delete a character without being authenticated");
            connection.Close();
            return;       
        }

        if (connection.Character != null)
        {
            logger.LogWarning("Connection tried to delete a character while already having a character selected");
            connection.Close();
            return;
        }
        
        // because this is an async method, the executtion of this should run in a separate thread, and the result to be put somewhere
        // so that this world connection when it calls 'ProcessQueryCallbacks()' it will get the result and react accordingly

        connection.AddQueryCallback(characterRepository.FindByIdAndAccountAsync(packet.CharacterId, connection.AccountId), character =>
        {
            OnCharacterFound(connection, character);
        });
        
    }

    private void OnCharacterFound(WorldConnection connection, Character? character)
    {
        if (character == null)
        {
            logger.LogWarning("Character not found for account {AccountId}", connection.AccountId);
            connection.Send(SCharacterDeletedPacket.Create(SCharacterDeletedResult.InternalError, connection.CryptoSession.Encrypt));
            return;
        }

        connection.AddQueryCallback(characterRepository.DeleteAsync(character.Id), () => {
            OnCharacterDeleted(connection, character);
        });
    }

    private void OnCharacterDeleted(WorldConnection connection, Character character)
    {
        logger.LogInformation("Character {CharacterId} deleted for account {AccountId}", character.Id, connection.AccountId);
        connection.Send(SCharacterDeletedPacket.Create(SCharacterDeletedResult.Success, connection.CryptoSession.Encrypt));
    }
}