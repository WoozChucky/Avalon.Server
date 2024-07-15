using Avalon.Database.Character.Repositories;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class CharacterDeleteHandler : IWorldPacketHandler<CCharacterDeletePacket>
{
    private readonly ILogger<CharacterDeleteHandler> _logger;
    private readonly ICharacterRepository _characterRepository;
    
    public CharacterDeleteHandler(ILogger<CharacterDeleteHandler> logger, ICharacterRepository characterRepository)
    {
        _logger = logger;
        _characterRepository = characterRepository;
    }
    
    public async Task ExecuteAsync(WorldPacketContext<CCharacterDeletePacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection.AccountId == null)
        {
            _logger.LogWarning("Connection tried to delete a character without being authenticated");
            ctx.Connection.Close();
            return;       
        }

        if (ctx.Connection.Character != null)
        {
            _logger.LogWarning("Connection tried to delete a character while already having a character selected");
            ctx.Connection.Close();
            return;
        }
        
        var character = await _characterRepository.FindByIdAndAccountAsync(ctx.Packet.CharacterId, ctx.Connection.AccountId);
        if (character == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for account {AccountId}", ctx.Packet.CharacterId, ctx.Connection.AccountId);
            ctx.Connection.Send(SCharacterDeletedPacket.Create(SCharacterDeletedResult.InternalError, ctx.Connection.CryptoSession.Encrypt));
            return;
        }

        await _characterRepository.DeleteAsync(character.Id);
        
        _logger.LogInformation("Character {CharacterId} deleted for account {AccountId}",ctx.Packet.CharacterId, ctx.Connection.AccountId);
        
        ctx.Connection.Send(SCharacterDeletedPacket.Create(SCharacterDeletedResult.Success,ctx.Connection.CryptoSession.Encrypt));
    }
}
