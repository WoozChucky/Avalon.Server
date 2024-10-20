using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_LIST)]
public class CharacterListHandler(
    ILogger<CharacterListHandler> logger,
    ICharacterRepository characterRepository,
    IWorld world) : WorldPacketHandler<CCharacterListPacket>
{

    public override void Execute(WorldConnection connection, CCharacterListPacket packet)
    {
        if (connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to request character list without being authenticated");
            connection.Close();
            return;
        }

        if (connection.Character != null)
        {
            logger.LogWarning("Connection tried to request character list while already having a character selected");
            connection.Close();
            return;
        }

        connection.AddQueryCallback(characterRepository.FindByAccountAsync(connection.AccountId), characters =>
        {
            OnCharactersReceived(connection, characters);
        });
    }

    private void OnCharactersReceived(WorldConnection connection, IList<Character> characters)
    {
        var characterInfo = characters.Select<Character, CharacterInfo>(
            character => new CharacterInfo
            {
                CharacterId = character.Id!.Value,
                Name = character.Name,
                Level = character.Level,
                Class = (ushort)character.Class,
                X = character.X,
                Y = character.Y,
                Z = character.Z,
            }).ToArray();

        var result = SCharacterListPacket.Create(
            characterInfo.Length,
            world.Configuration.MaxCharactersPerAccount,
            characterInfo,
            connection.CryptoSession.Encrypt
        );

        connection.Send(result);
    }
}
