using Avalon.Common.Mathematics;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;
using Avalon.World.Spells;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_SELECTED)]
public class CharacterSelectHandler(
    ILogger<CharacterSelectHandler> logger, 
    ILoggerFactory loggerFactory, 
    ICharacterRepository characterRepository,
    ICharacterInventoryRepository characterInventoryRepository,
    ICharacterSpellRepository characterSpellRepository,
    IWorld world) : WorldPacketHandler<CCharacterSelectedPacket>
{
    public override void Execute(WorldConnection connection, CCharacterSelectedPacket packet)
    {
        if (connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to select a character list without being authenticated");
            connection.Close();
            return;       
        }

        if (connection.Character != null)
        {
            logger.LogWarning("Connection tried to select a character list while already having a character selected");
            connection.Close();
            return;
        }
        
        connection.AddQueryCallback(characterRepository.FindByIdAndAccountAsync(packet.CharacterId, connection.AccountId), character =>
        {
            OnCharacterReceived(connection, character);
        });
    }

    private void OnCharacterReceived(WorldConnection connection, Character? character)
    {
        if (character == null)
        {
            logger.LogWarning("Character not found for account {AccountId}", connection.AccountId);
            return;
        }
        
        //TODO: Implement when instances are a thing
        character.InstanceId = Guid.NewGuid().ToString();
        character.Online = true;
        character.Latency = (int) connection.Latency;
        
        var entity = new CharacterEntity(loggerFactory, connection, character)
        {
            Data = character,
            Position = new Vector3(character.X, character.Y, character.Z),
            Velocity = Vector3.zero,
            Orientation = new Vector3(0, character.Rotation, 0),
            EnteredWorld = DateTime.UtcNow,
        };
        
        entity.CurrentHealth = entity.Health;
        entity.CurrentPower = entity.Power;
        
        
        connection.Character = entity;

        try
        {
            // TODO: Spawn in the world
            world.SpawnPlayer(connection);
        }
        catch (Exception e)
        {
            connection.Character = null;
            logger.LogError(e, "Error while spawning player {CharacterId}", character.Id);
            return;
        }
        
        var characterInfo = new CharacterInfo
        {
            CharacterId = character.Id,
            Name = character.Name,
            Level = character.Level,
            Class = (ushort) character.Class,
            X = character.X,
            Y = character.Y,
            Z = character.Z,
            Orientation = character.Rotation,
            Running = character.Running,
        };
        
        var mapInfo = new MapInfo
        {
            MapId = character.Map,
            InstanceId = Guid.Parse(character.InstanceId),
            Name = "Test Map",
            Description = "Test Map Description",
        };
        
        connection.Send(SCharacterSelectedPacket.Create(characterInfo, mapInfo, connection.CryptoSession.Encrypt));

        connection.AddQueryCallback(characterRepository.UpdateAsync(character), _ =>
        {
            logger.LogInformation("Character {CharacterId} logged in for account {AccountId} at {Position}", character.Name, connection.AccountId, connection.Character.Position);
            
            connection.AddQueryCallback(characterInventoryRepository.GetByCharacterIdAsync(character.Id), items =>
            {
                OnInventoryReceived(connection, character, items);
            });
        });
    }

    private void OnInventoryReceived(WorldConnection connection, Character character, IReadOnlyCollection<CharacterInventory> items)
    {
        var entity = connection.Character;
        if (entity == null)
        {
            logger.LogWarning("Character entity is null for account {AccountId}", connection.AccountId);
            return;
        }
        
        entity[InventoryType.Equipment].Load(items.Where(i => i.Container == InventoryType.Equipment).ToList());
        entity[InventoryType.Bag].Load(items.Where(i => i.Container == InventoryType.Bag).ToList());
        entity[InventoryType.Bank].Load(items.Where(i => i.Container == InventoryType.Bank).ToList());
        
        connection.AddQueryCallback(characterSpellRepository.GetCharacterSpellsAsync(character.Id), spells =>
        {
            OnSpellsReceived(connection, spells);
        });
    }
    
    private void OnSpellsReceived(WorldConnection connection, IReadOnlyCollection<CharacterSpell> spells)
    {
        var gameSpells = new List<GameSpell>();

        foreach (var characterSpell in spells)
        {
            var template = world.Data.SpellTemplates.FirstOrDefault(sp => sp.Id == characterSpell.SpellId);
            if (template == null)
            {
                logger.LogWarning("Spell template not found for spell {SpellId}", characterSpell.SpellId);
                continue;
            }

            var gameSpell = new GameSpell
            {
                SpellId = characterSpell.SpellId,
                Metadata = new SpellMetadata
                {
                    Name = template.Name,
                    Cooldown = (float) template.Cooldown / 1000,
                    CastTime = (float) template.CastTime / 1000,
                    Cost = template.Cost,
                    Range = template.Range,
                    Effects = template.Effects,
                    EffectValue = template.EffectValue,
                    ScriptName = template.SpellScript,
                },
                CastTimeTimer = (float) template.CastTime / 1000,
                CooldownTimer = characterSpell.Cooldown,
            };

            gameSpells.Add(gameSpell);
        }
        
        connection.Character!.Spells.Load(gameSpells);
        
        // TODO: Send spells to the client
    }
}
