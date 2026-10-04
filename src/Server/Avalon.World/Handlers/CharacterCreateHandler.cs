using Avalon.Common.GameAuth;
using Avalon.World.Persistence;
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
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_CREATE)]
public sealed class CharacterCreateHandler(ILogger<CharacterCreateHandler> logger, ICharacterRepository characterRepository,
    ICharacterStatsRepository characterStatsRepository, ICharacterAbilityRepository characterAbilityRepository,
    ICharacterInventoryRepository characterInventoryRepository, IItemInstanceRepository itemInstanceRepository,
    IItemIdAllocator itemIds, IWorld world) : WorldPacketHandler<CCharacterCreatePacket>
{
    public override void Execute(IWorldConnection connection, CCharacterCreatePacket packet)
    {
        if (connection.AccountId is null || connection.Character is not null || connection.PendingSpawn is not null ||
            connection.SelectInProgress || connection.LeaveInProgress || connection.IsClosing)
        { connection.Close(); return; }
        if (packet.Gender is < byte.MinValue or > byte.MaxValue || !Enum.IsDefined((CharacterGender)(byte)packet.Gender))
        { Answer(connection, SCharacterCreateResult.InvalidClass); return; }
        var nameProblem = CharacterName.Check(packet.Name);
        if (nameProblem != CharacterNameProblem.None)
        {
            Answer(connection, nameProblem switch
            {
                CharacterNameProblem.TooShort => SCharacterCreateResult.NameTooShort,
                CharacterNameProblem.TooLong => SCharacterCreateResult.NameTooLong,
                _ => SCharacterCreateResult.NameInvalid
            }); return;
        }
        if (connection.GameplayAuthority is not { } authority || authority.AccountId != connection.AccountId)
        { connection.Close(); return; }
        var createInfo = world.Data.CharacterCreateInfos.FirstOrDefault(c => c.Class == (CharacterClass)packet.Class);
        var level = world.Data.ClassLevelStats.FirstOrDefault(c => c.Class == (CharacterClass)packet.Class && c.Level == 1);
        if (createInfo is null || level is null) { Answer(connection, SCharacterCreateResult.InternalDatabaseError); return; }
        var stats = CharacterStatsCalculator.Calculate(level, [], world.Data.Combat.Factors[createInfo.Class]);
        var row = new Character
        {
            AccountId = authority.AccountId, Name = CharacterName.Display(packet.Name), Level = level.Level,
            Class = createInfo.Class, Gender = (CharacterGender)(byte)packet.Gender,
            X = createInfo.X, Y = createInfo.Y, Z = createInfo.Z, Rotation = createInfo.Rotation, Map = createInfo.Map,
            CreationDate = DateTime.UtcNow, Health = (int)Math.Min(stats.MaxHealth, (uint)int.MaxValue),
            Power1 = (int)Math.Min(stats.MaxPower, (uint)int.MaxValue)
        };
        var items = new List<ItemInstance>(); var slots = new List<CharacterInventory>();
        foreach (var templateId in createInfo.StartingItems)
        {
            var template = world.Data.ItemTemplates.FirstOrDefault(t => t.Id == templateId);
            if (template is null)
            {
                logger.LogWarning("Starting item {TemplateId} not found", templateId); continue;
            }
            var item = new ItemInstance
            {
                Id = itemIds.Next(), TemplateId = template.Id, Count = template.Stackable ? template.MaxStackSize : 1,
                Durability = ItemInstanceDefaults.InitialDurability(template), UpdatedAt = DateTime.UtcNow
            };
            items.Add(item); slots.Add(new() { ItemId = item.Id, Container = InventoryType.Bag, Slot = (ushort)(slots.Count) });
        }
        var batch = new CharacterCreationBatch(row, stats.ToRow(new(0)),
            createInfo.StartingSpells.Select(id => new CharacterAbility { AbilityId = id }).ToArray(), items, slots);
        var work = WorldDatabaseWork.ThreadPool.Run(async () =>
        {
            try { return await characterRepository.CreateForGameplayAsync(authority, batch, world.Configuration.MaxCharactersPerAccount, CancellationToken.None); }
            catch (Exception error)
            {
                logger.LogWarning(error, "Character creation failed for account {AccountId}", authority.AccountId);
                return new CharacterCreationReply(Error: error is GameplayWriteRejectedException ? GameAuthErrors.AuthorityRevoked : GameAuthErrors.DatabaseUnavailable);
            }
        });
        connection.EnqueueContinuation(work, reply =>
        {
            if (!connection.IsConnected || connection.IsClosing) return;
            Answer(connection, reply.Error switch
            {
                null => SCharacterCreateResult.Success, GameAuthErrors.NameTaken => SCharacterCreateResult.NameAlreadyExists,
                GameAuthErrors.MaxCharacters => SCharacterCreateResult.MaxCharactersReached, _ => SCharacterCreateResult.InternalDatabaseError
            });
#pragma warning disable MA0045 // Tick continuations must not await socket cleanup.
            if (reply.Error == GameAuthErrors.AuthorityRevoked) connection.Close();
#pragma warning restore MA0045
        });
    }
    private static void Answer(IWorldConnection connection, SCharacterCreateResult result) =>
        connection.Send(SCharacterCreatedPacket.Create(result, connection.CryptoSession.Encrypt));
}
