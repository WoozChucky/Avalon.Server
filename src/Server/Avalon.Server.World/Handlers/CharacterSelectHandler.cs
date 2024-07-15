using Avalon.Common.Mathematics;
using Avalon.Database.Character.Repositories;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class CharacterSelectHandler(
    ILoggerFactory loggerFactory,
    IWorld world,
    ICharacterRepository characterRepository,
    ICharacterInventoryRepository characterInventoryRepository,
    ICharacterSpellRepository characterSpellRepository)
    : IWorldPacketHandler<CCharacterSelectedPacket>
{
    private readonly ILogger<CharacterSelectHandler> _logger = loggerFactory.CreateLogger<CharacterSelectHandler>();

    public async Task ExecuteAsync(WorldPacketContext<CCharacterSelectedPacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection.AccountId == null)
        {
            _logger.LogWarning("Connection tried to select a character list without being authenticated");
            ctx.Connection.Close();
            return;       
        }

        if (ctx.Connection.Character != null)
        {
            _logger.LogWarning("Connection tried to select a character list while already having a character selected");
            ctx.Connection.Close();
            return;
        }
       
        var character = await characterRepository.FindByIdAndAccountAsync(ctx.Packet.CharacterId, ctx.Connection.AccountId);
        if (character == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for account {AccountId}", ctx.Packet.CharacterId, ctx.Connection.AccountId);
            return;
        }
        
        //TODO: Implement when instances are a thing
        character.InstanceId = Guid.NewGuid().ToString();
        character.Online = true;
        character.Latency = (int) ctx.Connection.Latency;
        
        var entity = new CharacterEntity(loggerFactory)
        {
            Data = character,
            Position = new Vector3(character.X, character.Y, character.Z),
            Velocity = Vector3.zero,
            Orientation = new Vector3(0, character.Rotation, 0),
            EnteredWorld = DateTime.UtcNow,
        };
        
        ctx.Connection.Character = entity;

        try
        {
            await world.SpawnPlayerAsync(ctx.Connection);
        }
        catch (Exception e)
        {
            ctx.Connection.Character = null;
            _logger.LogError(e, "Error while spawning player {CharacterId}", character.Id);
            return;
        }
        
        ctx.Connection.Send(SCharacterSelectedPacket.Create(new CharacterInfo
        {
            CharacterId = character.Id!.Value,
            Name = character.Name,
            Level = character.Level,
            Class = (ushort) character.Class,
            X = character.X,
            Y = character.Y,
            Z = character.Z
        },
        new MapInfo
        {
            MapId = character.Map,
            InstanceId = Guid.Parse(character.InstanceId),
            Name = "Test Map",
            Description = "Test Map Description",
        }, ctx.Connection.CryptoSession.Encrypt));

        // save the character to the database
        await characterRepository.UpdateAsync(character);
        
        _logger.LogInformation("Character {CharacterId} logged in for account {AccountId} at {Position}", character.Name, ctx.Connection.AccountId, ctx.Connection.Character.Position);
        
        var items = await characterInventoryRepository.GetByCharacterIdAsync(character.Id);
        
        await entity[InventoryType.Equipment].LoadAsync(items.Where(i => i.Container == InventoryType.Equipment).ToList());
        await entity[InventoryType.Bag].LoadAsync(items.Where(i => i.Container == InventoryType.Bag).ToList());
        await entity[InventoryType.Bank].LoadAsync(items.Where(i => i.Container == InventoryType.Bank).ToList());
        
        var spells = await characterSpellRepository.GetCharacterSpellsAsync(character.Id);
        
        await entity.Spells.LoadAsync(spells);
        
        //TODO: Send the inventory to the client
    }
}
