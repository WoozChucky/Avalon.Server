using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_CREATE)]
public sealed class CharacterCreateHandler(ILogger<CharacterCreateHandler> logger, ICharacterRepository characterRepository,
    IItemRoller itemRoller, IWorld world) : WorldPacketHandler<CCharacterCreatePacket>
{
    public override void Execute(IWorldConnection connection, CCharacterCreatePacket packet)
    {
        if (connection.AccountId is null || connection.Character is not null || connection.PendingSpawn is not null ||
            connection.SelectInProgress || connection.LeaveInProgress || connection.IsClosing)
        { connection.Close(); return; }
        // A protobuf enum member carries any number that fits it, so the class (None included) and gender are checked here.
        if (!CharacterClasses.IsPlayable(packet.Class) || !Enum.IsDefined(packet.Gender))
        { Answer(connection, SCharacterCreateResult.InvalidClass); return; }
        CharacterNameProblem nameProblem = CharacterName.Check(packet.Name);
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
        CharacterCreateInfo? createInfo = world.Data.CharacterCreateInfos.FirstOrDefault(c => c.Class == packet.Class);
        ClassLevelStat? level = world.Data.ClassLevelStats.FirstOrDefault(c => c.Class == packet.Class && c.Level == 1);
        if (createInfo is null || level is null) { Answer(connection, SCharacterCreateResult.InternalDatabaseError); return; }
        CombatPatch combat = world.Data.Combat;
        DerivedCharacterStats stats = CharacterStatsCalculator.Calculate(level, [], combat.Factors[createInfo.Class], combat.Formula);
        var row = new Character
        {
            AccountId = authority.AccountId,
            Name = CharacterName.Display(packet.Name),
            Level = level.Level,
            Class = createInfo.Class,
            Gender = packet.Gender,
            X = createInfo.X,
            Y = createInfo.Y,
            Z = createInfo.Z,
            Rotation = createInfo.Rotation,
            Map = createInfo.Map,
            CreationDate = DateTime.UtcNow,
            Health = (int)Math.Min(stats.MaxHealth, (uint)int.MaxValue),
            Power1 = (int)Math.Min(stats.MaxPower, (uint)int.MaxValue)
        };
        var items = new List<ItemInstance>(); var slots = new List<CharacterInventory>();
        ItemsPatch itemData = world.Data.Items;
        foreach (ItemTemplateId templateId in createInfo.StartingItems)
        {
            ItemTemplate? template = itemData.Find(templateId);
            if (template is null)
            {
                logger.LogWarning("Starting item {TemplateId} not found", templateId); continue;
            }
            // #1179: a starting item is made as any copy is, rolling its base stats and the modifiers its template links;
            // they are inserted with it. A copy that rolled either is one of a kind, so it is never a stack.
            RolledItem rolled = itemRoller.RollPlain(template, ItemInstanceDefaults.InitialItemLevel(template), itemData);
            var item = new ItemInstance
            {
                Id = rolled.InstanceId,
                TemplateId = template.Id,
                Count = template.Stackable && !rolled.HasRolledRows ? template.MaxStackSize : 1,
                Durability = ItemInstanceDefaults.InitialDurability(template),
                Rarity = rolled.Rarity,
                ItemLevel = rolled.ItemLevel,
                // Starting items are identified (#1179), whatever they rolled.
                Identified = true,
                UpdatedAt = DateTime.UtcNow,
            };
            // The copy's own rows, inserted with it; one that rolled none keeps the instance's own empty list, since the roll
            // shares one for every copy without modifiers.
            if (rolled.Modifiers.Count > 0)
                item.Modifiers = rolled.Modifiers;
            foreach ((ItemBaseStat stat, int value) in rolled.BaseStats)
                item.BaseStats.Add(new ItemInstanceBaseStat { ItemInstanceId = rolled.InstanceId, Stat = stat, Value = value });
            items.Add(item); slots.Add(new() { ItemId = item.Id, Container = InventoryType.Bag, Slot = (byte)(slots.Count) });
        }
        var batch = new CharacterCreationBatch(row, stats.ToRow(new(0)),
            createInfo.StartingSpells.Select(id => new CharacterAbility { AbilityId = id }).ToArray(), items, slots);
        Task<CharacterCreationReply> work = WorldDatabaseWork.ThreadPool.Run(async () =>
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
                null => SCharacterCreateResult.Success,
                GameAuthErrors.NameTaken => SCharacterCreateResult.NameAlreadyExists,
                GameAuthErrors.MaxCharacters => SCharacterCreateResult.MaxCharactersReached,
                _ => SCharacterCreateResult.InternalDatabaseError
            });
#pragma warning disable MA0045 // Tick continuations must not await socket cleanup.
            if (reply.Error == GameAuthErrors.AuthorityRevoked) connection.Close();
#pragma warning restore MA0045
        });
    }
    private static void Answer(IWorldConnection connection, SCharacterCreateResult result) =>
        connection.Send(SCharacterCreatedPacket.Create(result, PacketEncoder.Shared));
}
