using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Database.Repositories;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_CREATE)]
public class CharacterCreateHandler(
    ILogger<CharacterCreateHandler> logger,
    ICharacterRepository characterRepository,
    ICharacterStatsRepository characterStatsRepository,
    ICharacterSpellRepository characterSpellRepository,
    ICharacterInventoryRepository characterInventoryRepository,
    IItemInstanceRepository itemInstanceRepository,
    IWorld world)
    : WorldPacketHandler<CCharacterCreatePacket>
{

    public override void Execute(WorldConnection connection, CCharacterCreatePacket packet)
    {
        if (connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to create a character without being authenticated");
            connection.Close();
            return;
        }

        if (connection.Character != null)
        {
            logger.LogWarning("Connection tried to create a character while already having a character selected");
            connection.Close();
            return;
        }

        connection.AddQueryCallback(characterRepository.FindByAccountAsync(connection.AccountId), characters =>
        {
            OnCharactersReceived(connection, characters, packet);
        });
    }

    private void OnCharactersReceived(WorldConnection connection, IList<Character> characters, CCharacterCreatePacket packet)
    {
        var currentCharacterCount = characters.Count;
        
        if (currentCharacterCount == world.Configuration.MaxCharactersPerAccount || currentCharacterCount + 1 > world.Configuration.MaxCharactersPerAccount)
        {
            logger.LogDebug("Account {AccountId} already has {CharacterCount} characters", connection.AccountId, currentCharacterCount);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.MaxCharactersReached, connection.CryptoSession.Encrypt));
            return;
        }
        
        connection.AddQueryCallback(characterRepository.FindByNameAsync(packet.Name), character => {
            OnDuplicateCharacterReceived(connection, character, packet);
        });
    }
    
    private void OnDuplicateCharacterReceived(WorldConnection connection, Character? duplicateCharacter, CCharacterCreatePacket packet)
    {
        if (duplicateCharacter != null)
        {
            logger.LogDebug("Character {Name} already exists", packet.Name);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameAlreadyExists, connection.CryptoSession.Encrypt));
            return;
        }
        
        if (packet.Name.Length < 3)
        {
            logger.LogDebug("Character name {Name} is too short", packet.Name);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameTooShort, connection.CryptoSession.Encrypt));
            return;
        }
        
        if (packet.Name.Length > 12)
        {
            logger.LogDebug("Character name {Name} is too long", packet.Name);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameTooLong, connection.CryptoSession.Encrypt));
            return;
        }
        
        var createInfo = world.Data.CharacterCreateInfos.FirstOrDefault(c => c.Class == (CharacterClass) packet.Class);
        if (createInfo == null)
        {
            logger.LogWarning("Character class {Class} does not have a creation info", packet.Class);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.InternalDatabaseError, connection.CryptoSession.Encrypt));
            return;
        }
        
        var classLevelStats = world.Data.ClassLevelStats.FirstOrDefault(c => c.Class == (CharacterClass) packet.Class && c.Level == 1);
        if (classLevelStats == null)
        {
            logger.LogWarning("Character class {Class} does not have a level 1 stat info", packet.Class);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.InternalDatabaseError, connection.CryptoSession.Encrypt));
            return;
        }
        
        var @class = (CharacterClass) packet.Class;
        
        var character = new Character
        {
            AccountId = connection.AccountId!.Value,
            Name = packet.Name,
            Level = classLevelStats.Level,
            Class = createInfo.Class,
            X = createInfo.X,
            Y = createInfo.Y,
            Z = createInfo.Z,
            Rotation = createInfo.Rotation,
            Map = createInfo.Map,
            CreationDate = DateTime.UtcNow,
            Health = (int) CharacterStats.GetBaseHp(@class, classLevelStats.Stamina, classLevelStats.Level),
            Power1 = (int) CharacterStats.GetBasePower(@class, classLevelStats.Intellect, classLevelStats.Agility, classLevelStats.Level),
            Power2 = 0,
            Experience = 0,
        };
            
        connection.AddQueryCallback(characterRepository.CreateAsync(character), createdCharacter =>
        {
            OnCharacterCreated(connection, createdCharacter, classLevelStats, createInfo.Class, createInfo);
        });
    }

    private void OnCharacterCreated(WorldConnection connection, Character character, ClassLevelStat classLevelStat,
        CharacterClass @class, CharacterCreateInfo createInfo)
    {
        var characterStats = new CharacterStats
        {
            Character = character,
            CharacterId = character.Id,
            MaxHealth = CharacterStats.GetBaseHp(@class, classLevelStat.Stamina, classLevelStat.Level),
            MaxPower1 = CharacterStats.GetBasePower(@class, classLevelStat.Intellect, classLevelStat.Agility, classLevelStat.Level),
            MaxPower2 = 0,
            Stamina = classLevelStat.Stamina,
            Strength = classLevelStat.Strength,
            Agility = classLevelStat.Agility,
            Intellect = classLevelStat.Intellect,
            Armor = 0,
            BlockPct = CharacterStats.GetBaseBlockPercent(@class),
            DodgePct = CharacterStats.GetBaseDodgePercent(@class),
            CritPct = CharacterStats.GetBaseCritPercent(@class),
            AttackDamage = CharacterStats.GetBaseAttackDamage(@class, classLevelStat.Strength, classLevelStat.Agility),
            AbilityDamage = CharacterStats.GetBaseAbilityDamage(@class, classLevelStat.Intellect),
        };
        
        connection.AddQueryCallback(characterStatsRepository.CreateAsync(characterStats), createdStats =>
        {
            OnCharacterStatsCreated(connection, character, createInfo);
        });
    }

    private void OnCharacterStatsCreated(WorldConnection connection, Character character, CharacterCreateInfo createInfo)
    {
        var characterSpellIds = createInfo.StartingSpells;
        
        var characterSpells = characterSpellIds.Select(spellId => new CharacterSpell {CharacterId = character.Id, SpellId = spellId,}).ToList();

        connection.AddQueryCallback(characterSpellRepository.CreateAsync(characterSpells), createdSpells =>
        {
            OnCharacterSpellsCreated(connection, character, createInfo);
        });
    }

    private void OnCharacterSpellsCreated(WorldConnection connection, Character character, CharacterCreateInfo createInfo)
    {
        var startingItems = createInfo.StartingItems;
        var itemInstances = new List<ItemInstance>();
        
        foreach (var startingItemId in startingItems)
        {
            var itemTemplate = world.Data.ItemTemplates.FirstOrDefault(i => i.Id == startingItemId);
            if (itemTemplate == null)
            {
                logger.LogWarning("Starting item {ItemTemplateId} not found", startingItemId);
                continue;
            }
            
            var durability = itemTemplate.Class switch
            {
                ItemClass.Weapon => 42U,
                ItemClass.Armor => 69U,
                _ => 0U
            };
                
            var itemInstance = new ItemInstance
            {
                Template = itemTemplate,
                TemplateId = itemTemplate.Id,
                CharacterId = character.Id,
                Count = itemTemplate.Stackable ? itemTemplate.MaxStackSize : 1,
                Charges = 0, // For future use
                Durability = durability,
                Flags = ItemInstanceFlags.None,
                UpdatedAt = DateTime.UtcNow,
            };
            
            itemInstances.Add(itemInstance);
        }
        
        connection.AddQueryCallback(itemInstanceRepository.CreateAsync(itemInstances), createdItems =>
        {
            if (createdItems.Count != itemInstances.Count)
            {
                logger.LogWarning("Character {Name} did not receive all starting item instances", character.Name);
                return;
            }
            
            var currentSlot = 0;
            var characterInventories = new List<CharacterInventory>();
            
            foreach (var itemInstance in createdItems)
            {
                var characterInventory = new CharacterInventory
                {
                    CharacterId = character.Id,
                    ItemId = itemInstance.Id,
                    Container = InventoryType.Bag,
                    Slot = (ushort) currentSlot++,
                };
                
                characterInventories.Add(characterInventory);
            }
            
            connection.AddQueryCallback(characterInventoryRepository.CreateAsync(characterInventories), charIventories =>
            {
                if (charIventories.Count != itemInstances.Count)
                {
                    logger.LogWarning("Character {Name} did not receive all starting items", character.Name);
                    return;
                }
                
                OnCharacterItemsCreated(connection, character);
            });
        });
    }

    private void OnCharacterItemsCreated(WorldConnection connection, Character character)
    {
        logger.LogInformation("Character {Name} created for account {AccountId}", character.Name, character.AccountId);
        
        connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.Success, connection.CryptoSession.Encrypt));
    }
}
