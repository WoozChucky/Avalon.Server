using Avalon.Database.Character.Repositories;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class CharacterListHandler : IWorldPacketHandler<CCharacterListPacket>
{
    private readonly ILogger<CharacterListHandler> _logger;
    private readonly ICharacterRepository _characterRepository;
    private readonly IWorld _world;

    public CharacterListHandler(ILogger<CharacterListHandler> logger, ICharacterRepository characterRepository, IWorld world)
    {
        _logger = logger;
        _characterRepository = characterRepository;
        _world = world;
    }
    
    public async Task ExecuteAsync(WorldPacketContext<CCharacterListPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection.AccountId == null)
        {
            _logger.LogWarning("Connection tried to request character list without being authenticated");
            ctx.Connection.Close();
            return;       
        }

        if (ctx.Connection.CharacterId != null)
        {
            _logger.LogWarning("Connection tried to request character list while already having a character selected");
            ctx.Connection.Close();
            return;
        }

        var characters = await _characterRepository.FindByAccountAsync(ctx.Connection.AccountId);

        var characterInfo = characters.Select<Domain.Characters.Character, CharacterInfo>(
            character => new CharacterInfo
            {
                CharacterId = character.Id!.Value,
                Name = character.Name,
                Level = character.Level,
                Class = (ushort) character.Class,
                X = character.X,
                Y = character.Y,
            }).ToArray();
        
        var result = SCharacterListPacket.Create(
            characterInfo.Length, 
            _world.Configuration.MaxCharactersPerAccount, 
            characterInfo, 
            ctx.Connection.CryptoSession.Encrypt
        );
        
        ctx.Connection.Send(result);
    }
}
