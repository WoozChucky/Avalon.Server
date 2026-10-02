using Avalon.Combat;
using Avalon.Common;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_CREATE)]
public class CharacterCreateHandler(
    ILogger<CharacterCreateHandler> logger,
    ICharacterRepository characterRepository,
    ICharacterStatsRepository characterStatsRepository,
    ICharacterAbilityRepository characterAbilityRepository,
    ICharacterInventoryRepository characterInventoryRepository,
    IItemInstanceRepository itemInstanceRepository,
    IItemIdAllocator itemIds,
    IWorld world)
    : WorldPacketHandler<CCharacterCreatePacket>
{

    public override void Execute(IWorldConnection connection, CCharacterCreatePacket packet)
    {
        if (connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to create a character without being authenticated");
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
            logger.LogWarning("Connection tried to create a character while already having a character selected");
            connection.Close();
            return;
        }

        // The gender arrives from the client, so it is checked before anything is read or written.
        // A client that omits it sends 0, which is Male.
        if (!IsDefinedGender(packet.Gender))
        {
            logger.LogDebug("Character gender {Gender} is not a defined value", packet.Gender);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.InvalidClass, connection.CryptoSession.Encrypt));
            return;
        }

        // The name too (#757): 3 to 12 ASCII letters, exactly as sent, before anything is read.
        if (NameRefusal(CharacterName.Check(packet.Name)) is { } refusal)
        {
            logger.LogDebug("Character name {Name} breaks the name rule: {Refusal}", packet.Name, refusal);
            connection.Send(SCharacterCreatedPacket.Create(refusal, connection.CryptoSession.Encrypt));
            return;
        }

        connection.EnqueueContinuation(characterRepository.FindByAccountAsync(connection.AccountId), characters =>
        {
            OnCharactersReceived(connection, characters, packet);
        });
    }

    private static SCharacterCreateResult? NameRefusal(CharacterNameProblem problem) => problem switch
    {
        CharacterNameProblem.None => null,
        CharacterNameProblem.TooShort => SCharacterCreateResult.NameTooShort,
        CharacterNameProblem.TooLong => SCharacterCreateResult.NameTooLong,
        _ => SCharacterCreateResult.NameInvalid,
    };

    /// <summary>
    /// Inserts the character, or answers null when the name was taken in the meantime. The duplicate check before it
    /// and this insert are not atomic: a character whose name differs only in case can be created in between, and the
    /// unique index on NameKey (#757) refuses this one. Any other failure is rethrown.
    /// </summary>
    private async Task<Character?> CreateUnlessNameTakenAsync(Character character)
    {
        try
        {
            return await characterRepository.CreateAsync(character, CancellationToken.None);
        }
        catch (DbUpdateException)
        {
            if (await characterRepository.FindByNameAsync(character.Name, CancellationToken.None) is not null)
                return null;
            throw;
        }
    }

    /// <summary>
    /// Range-checks before casting: <see cref="CharacterGender"/> is a byte, and casting an
    /// out-of-range int to it would wrap (256 reads as Male) instead of failing.
    /// </summary>
    private static bool IsDefinedGender(int raw) =>
        raw is >= byte.MinValue and <= byte.MaxValue && Enum.IsDefined((CharacterGender)(byte)raw);

    private void OnCharactersReceived(IWorldConnection connection, IList<Character> characters, CCharacterCreatePacket packet)
    {
        var currentCharacterCount = characters.Count;

        if (currentCharacterCount == world.Configuration.MaxCharactersPerAccount || currentCharacterCount + 1 > world.Configuration.MaxCharactersPerAccount)
        {
            logger.LogDebug("Account {AccountId} already has {CharacterCount} characters", connection.AccountId, currentCharacterCount);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.MaxCharactersReached, connection.CryptoSession.Encrypt));
            return;
        }

        connection.EnqueueContinuation(characterRepository.FindByNameAsync(packet.Name, CancellationToken.None), character =>
        {
            OnDuplicateCharacterReceived(connection, character, packet);
        });
    }

    private void OnDuplicateCharacterReceived(IWorldConnection connection, Character? duplicateCharacter, CCharacterCreatePacket packet)
    {
        if (duplicateCharacter != null)
        {
            logger.LogDebug("Character {Name} already exists", packet.Name);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameAlreadyExists, connection.CryptoSession.Encrypt));
            return;
        }

        var createInfo = world.Data.CharacterCreateInfos.FirstOrDefault(c => c.Class == (CharacterClass)packet.Class);
        if (createInfo == null)
        {
            logger.LogWarning("Character class {Class} does not have a creation info", packet.Class);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.InternalDatabaseError, connection.CryptoSession.Encrypt));
            return;
        }

        var classLevelStats = world.Data.ClassLevelStats.FirstOrDefault(c => c.Class == (CharacterClass)packet.Class && c.Level == 1);
        if (classLevelStats == null)
        {
            logger.LogWarning("Character class {Class} does not have a level 1 stat info", packet.Class);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.InternalDatabaseError, connection.CryptoSession.Encrypt));
            return;
        }

        var gender = (CharacterGender)(byte)packet.Gender; // range-checked in Execute

        // Every starting item goes to the Bag, so a new character wears nothing yet.
        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(classLevelStats, [],
            world.Data.Combat.Factors[createInfo.Class]);

        var character = new Character
        {
            AccountId = connection.AccountId!.Value,
            Name = CharacterName.Display(packet.Name), // "kAELA" is stored, and shown, as "Kaela" (#757)
            Level = classLevelStats.Level,
            Class = createInfo.Class,
            Gender = gender,
            X = createInfo.X,
            Y = createInfo.Y,
            Z = createInfo.Z,
            Rotation = createInfo.Rotation,
            Map = createInfo.Map,
            CreationDate = DateTime.UtcNow,
            Health = (int)Math.Min(stats.MaxHealth, (uint)int.MaxValue), // #506: the row is an int; clamp, never wrap
            Power1 = (int)Math.Min(stats.MaxPower, (uint)int.MaxValue),
            Power2 = 0,
            Experience = 0,
        };

        connection.EnqueueContinuation(CreateUnlessNameTakenAsync(character), createdCharacter =>
        {
            if (createdCharacter is null)
            {
                logger.LogDebug("Character {Name} was created by another request first", character.Name);
                connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameAlreadyExists, connection.CryptoSession.Encrypt));
                return;
            }

            OnCharacterCreated(connection, createdCharacter, stats, createInfo);
        });
    }

    private void OnCharacterCreated(IWorldConnection connection, Character character, DerivedCharacterStats stats,
        CharacterCreateInfo createInfo)
    {
        CharacterStats characterStats = stats.ToRow(character.Id);

        connection.EnqueueContinuation(characterStatsRepository.CreateAsync(characterStats, CancellationToken.None), createdStats =>
        {
            OnCharacterStatsCreated(connection, character, createInfo);
        });
    }

    private void OnCharacterStatsCreated(IWorldConnection connection, Character character, CharacterCreateInfo createInfo)
    {
        var characterSpellIds = createInfo.StartingSpells;

        var characterAbilities = characterSpellIds.Select(abilityId => new CharacterAbility { CharacterId = character.Id, AbilityId = abilityId, }).ToList();

        connection.EnqueueContinuation(characterAbilityRepository.CreateAsync(characterAbilities, CancellationToken.None), createdSpells =>
        {
            OnCharacterSpellsCreated(connection, character, createInfo);
        });
    }

    private void OnCharacterSpellsCreated(IWorldConnection connection, Character character, CharacterCreateInfo createInfo)
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

            var durability = ItemInstanceDefaults.InitialDurability(itemTemplate);

            var itemInstance = new ItemInstance
            {
                Id = itemIds.Next(),
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

        connection.EnqueueContinuation(itemInstanceRepository.CreateAsync(itemInstances, CancellationToken.None), createdItems =>
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
                    Slot = (ushort)currentSlot++,
                };

                characterInventories.Add(characterInventory);
            }

            connection.EnqueueContinuation(characterInventoryRepository.CreateAsync(characterInventories, CancellationToken.None), charIventories =>
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

    private void OnCharacterItemsCreated(IWorldConnection connection, Character character)
    {
        logger.LogInformation("Character {Name} created for account {AccountId}", character.Name, character.AccountId);

        connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.Success, connection.CryptoSession.Encrypt));
    }
}

