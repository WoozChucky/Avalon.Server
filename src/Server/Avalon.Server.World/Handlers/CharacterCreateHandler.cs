using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class CharacterCreateHandler : IWorldPacketHandler<CCharacterCreatePacket>
{
    private readonly ILogger<CharacterCreateHandler> _logger;
    private readonly ICharacterRepository _characterRepository;
    private readonly IWorld _world;
    
    public CharacterCreateHandler(ILogger<CharacterCreateHandler> logger, ICharacterRepository characterRepository, IWorld world)
    {
        _logger = logger;
        _characterRepository = characterRepository;
        _world = world;
    }
    
    public async Task ExecuteAsync(WorldPacketContext<CCharacterCreatePacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection.AccountId == null)
        {
            _logger.LogWarning("Connection tried to create a character without being authenticated");
            ctx.Connection.Close();
            return;       
        }

        if (ctx.Connection.CharacterId != null)
        {
            _logger.LogWarning("Connection tried to create a character while already having a character selected");
            ctx.Connection.Close();
            return;
        }
        
        var encrypt = ctx.Connection.CryptoSession.Encrypt;
        
        var characters = await _characterRepository.FindByAccountAsync(ctx.Connection.AccountId);
        
        var currentCharacterCount = characters.Count;
        
        if (currentCharacterCount == _world.Configuration.MaxCharactersPerAccount || currentCharacterCount + 1 > _world.Configuration.MaxCharactersPerAccount)
        {
            _logger.LogDebug("Account {AccountId} already has {CharacterCount} characters", ctx.Connection.AccountId, currentCharacterCount);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.MaxCharactersReached, encrypt));
            return;
        }
        
        var duplicateCharacter = await _characterRepository.FindByNameAsync(ctx.Packet.Name);
        if (duplicateCharacter != null)
        {
            _logger.LogDebug("Character {Name} already exists", ctx.Packet.Name);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameAlreadyExists, encrypt));
            return;
        }
        
        if (ctx.Packet.Name.Length < 3)
        {
            _logger.LogDebug("Character name {Name} is too short", ctx.Packet.Name);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameTooShort, encrypt));
            return;
        }
        
        if (ctx.Packet.Name.Length > 12)
        {
            _logger.LogDebug("Character name {Name} is too long", ctx.Packet.Name);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameTooLong, encrypt));
            return;
        }
        
        var character = new Character
        {
            AccountId = ctx.Connection.AccountId!.Value,
            Name = ctx.Packet.Name, // TODO: Sanitize name
            Level = 1,
            Class = (CharacterClass) ctx.Packet.Class,
            X = 326, // TODO: Get starting position from starting map
            Y = 1450, // TODO: Get starting position from starting map
            Map = 1
        };

        try
        {
            await _characterRepository.CreateAsync(character);
            _logger.LogInformation("Character {Name} created for account {AccountId}", ctx.Packet.Name, encrypt);
            
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.Success, encrypt));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to create character {Name}", ctx.Packet.Name);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.InternalDatabaseError, encrypt));
        }
    }
}
