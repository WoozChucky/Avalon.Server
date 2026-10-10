using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Vendor;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;

namespace Avalon.World.Vendors;

/// <summary>
/// SMSG_VENDOR_LIST (spec #432): what an open shop offers one player, and what that player can buy
/// back. It is always the whole list. A list goes out when the shop opens, from
/// DialogueChooseHandler, and after that from the instance's vendor pass on the tick, through
/// <see cref="SendIfOwed" />. Handlers never send one themselves.
/// </summary>
public static class VendorListBuilder
{
    /// <summary>
    /// Rows in Sequence order. A row is left out when its quest gate is not met, or when its item
    /// template is gone (a /reload items removed it) or is now a quest item (a /reload items flagged it).
    /// The buyback newest first, each with its index, leaving out a sale whose item is now a quest item (#738).
    /// </summary>
    public static SVendorListPacket Build(
        ObjectGuid vendor, VendorStockState stock, CharacterEntity character, StaticData data, IQuestProgress quests)
    {
        List<VendorEntryDto> entries = new(stock.Rows.Count);
        foreach (VendorStockView row in stock.Rows)
        {
            if (!VendorRules.IsVisible(row, character, quests) || FindTemplate(data, row.ItemTemplateId) is not { } item
                || !VendorRules.IsStockable(item))
            {
                continue;
            }

            entries.Add(new VendorEntryDto
            {
                Sequence = row.Sequence,
                ItemTemplateId = row.ItemTemplateId.Value,
                Price = VendorRules.UnitPrice(row, item),
                Stock = stock.Available(row),
                Costs = row.Costs
                    .Select(c => new VendorCostDto { ItemTemplateId = c.ItemTemplateId.Value, Count = c.Count })
                    .ToArray(),
            });
        }

        // A sale whose item is now a quest item (#738) is left out; the others keep the index a buyback names.
        IReadOnlyList<BuybackEntry> sold = character.Buyback.Entries;
        List<VendorBuybackDto> buyback = new(sold.Count);
        for (int index = 0; index < sold.Count; index++)
        {
            if (FindTemplate(data, sold[index].Item.TemplateId) is { } template && !VendorRules.IsStockable(template))
                continue;

            buyback.Add(new VendorBuybackDto
            {
                Index = (uint)index,
                Item = ItemSlotDtoMapper.ToDto(InventoryType.Bag, sold[index].Item),
                Price = sold[index].Price,
            });
        }

        return new SVendorListPacket { VendorGuid = vendor.RawValue, Entries = entries.ToArray(), Buyback = buyback.ToArray() };
    }

    /// <summary>Sends the whole list and settles what the player was owed.</summary>
    public static void Send(
        IWorldConnection connection, ObjectGuid vendor, VendorStockState stock, CharacterEntity character,
        StaticData data, IQuestProgress quests)
    {
        SVendorListPacket list = Build(vendor, stock, character, data, quests);
        connection.Send(SVendorListPacket.Create(list.VendorGuid, list.Entries, list.Buyback, PacketEncoder.Shared));
        character.VendorListOwed = false;
        character.VendorListQuestVersion = character.Quests.HeldVersion;
    }

    /// <summary>
    /// The vendor pass, for one connection. It sends the list when the shop is open and the vendor's
    /// stock changed this tick, this player's buyback did, or which quests it holds or has turned in
    /// did since the last list (#738: an accept, an abandon or a turn-in can meet a row's quest gate).
    /// A closed shop forgets what it was owed. Allocation-free when nothing is owed.
    /// </summary>
    public static void SendIfOwed(IWorldConnection connection, VendorStocks stocks, StaticData data, IQuestProgress quests)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        if (character.OpenShopNpc is not { } vendor
            || !ShopAccess.IsOpen(connection, character)
            || !stocks.TryGet(vendor, out VendorStockState? stock))
        {
            character.VendorListOwed = false;
            return;
        }

        if (stock.Changed || character.VendorListOwed || character.VendorListQuestVersion != character.Quests.HeldVersion)
            Send(connection, vendor, stock, character, data, quests);
    }

    private static ItemTemplate? FindTemplate(StaticData data, ItemTemplateId id) =>
        data.ItemTemplates.FirstOrDefault(t => t.Id == id);
}
