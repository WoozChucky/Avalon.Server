using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Extracts one awake affix of a Bag item as a bound glyph at a Resonance Workbench (#1180 part C), on the tick, in the
/// map pass. Every request gets exactly one SMSG_ITEM_EXTRACT_RESULT; a connection with no character gets nothing.
/// ExtractRules decides; an Ok destroys the item through the inventory service and puts the glyph in its slot, and both
/// reach the client in the tick's SMSG_INVENTORY_UPDATE. It costs no gold. Nothing is held open between requests: each
/// one re-checks the Workbench and its range.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_ITEM_EXTRACT)]
public sealed class ItemExtractHandler(ILogger<ItemExtractHandler> logger, IWorld world, ICharacterEconomy economy)
    : WorldPacketHandler<CItemExtractPacket>
{
    public override void Execute(IWorldConnection connection, CItemExtractPacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_ITEM_EXTRACT from a connection with no character");
            return;
        }

        ItemExtractResult result;
        try
        {
            result = Extract(character, packet);
        }
        catch (Exception e)
        {
            // The request still gets its one reply, and nothing escapes onto the tick. The inventory service builds the
            // glyph before it destroys the item, so a failure leaves the item as it was.
            logger.LogError(e, "CMSG_ITEM_EXTRACT {RequestId} for Bag slot {BagSlot} threw; answering InternalError",
                packet.RequestId, packet.BagSlot);
            result = ItemExtractResult.InternalError;
        }

        connection.Send(SItemExtractResultPacket.Create(packet.RequestId, result, PacketEncoder.Shared));
    }

    private ItemExtractResult Extract(CharacterEntity character, CItemExtractPacket packet)
    {
        var host = world.InstanceRegistry.GetInstanceById(character.InstanceId) as IDoodadHost;
        ItemExtractResult result = ExtractRules.Decide(character, host, packet.WorkbenchGuid, packet.BagSlot,
            packet.ModifierId, world.Data.Items);
        if (result != ItemExtractResult.Ok)
            return result;

        // Decided a moment ago, on this thread, against this state; the rules refuse an id above int.MaxValue.
        if (!economy.InventoryOf(character).TryExtract(packet.BagSlot, (int)packet.ModifierId))
            throw new InvalidOperationException($"Extracting from Bag slot {packet.BagSlot} failed after ExtractRules allowed it.");

        return ItemExtractResult.Ok;
    }
}
