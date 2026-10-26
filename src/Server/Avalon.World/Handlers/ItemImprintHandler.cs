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
/// Imprints a glyph onto a Bag item at a Resonance Workbench (#1180 part C), on the tick, in the map pass. Every request
/// gets exactly one SMSG_ITEM_IMPRINT_RESULT; a connection with no character gets nothing. ImprintRules decides; an Ok
/// pays Game:Items:ImprintCost for the glyph's tier through the wallet (nothing when it is 0), then imprints through the
/// inventory service, which may brick the item (answered Bricked, a success: the gold and the glyph are spent). The item,
/// the emptied glyph slot and the gold reach the client in the tick's SMSG_INVENTORY_UPDATE. Nothing is held open
/// between requests: each one re-checks the Workbench and its range. The item's instability is never logged.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_ITEM_IMPRINT)]
public sealed class ItemImprintHandler(ILogger<ItemImprintHandler> logger, IWorld world, ICharacterEconomy economy)
    : WorldPacketHandler<CItemImprintPacket>
{
    public override void Execute(IWorldConnection connection, CItemImprintPacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_ITEM_IMPRINT from a connection with no character");
            return;
        }

        ItemImprintResult result;
        try
        {
            result = Imprint(character, packet);
        }
        catch (Exception e)
        {
            // The request still gets its one reply, and nothing escapes onto the tick. The gold is taken before the item
            // changes, so a failure caused by a bug loses value, never creates it, and is not rolled back; the inventory
            // service makes the new list before it changes anything, so the glyph and the item are left as they were.
            logger.LogError(e, "CMSG_ITEM_IMPRINT {RequestId} threw; answering InternalError", packet.RequestId);
            result = ItemImprintResult.InternalError;
        }

        connection.Send(SItemImprintResultPacket.Create(packet.RequestId, result, PacketEncoder.Shared));
    }

    private ItemImprintResult Imprint(CharacterEntity character, CItemImprintPacket packet)
    {
        var host = world.InstanceRegistry.GetInstanceById(character.InstanceId) as IDoodadHost;
        // The character's own, kept by the economy, so a request, refused or not, allocates nothing.
        IWallet wallet = economy.WalletOf(character);
        ItemImprintResult result = ImprintRules.Decide(character, host, packet.WorkbenchGuid, packet.GlyphBagSlot,
            packet.TargetBagSlot, world.Data.Items, world.Configuration.Items.ImprintCost, wallet, out ulong cost);
        if (result == ItemImprintResult.InternalError)
        {
            logger.LogError("CMSG_ITEM_IMPRINT {RequestId}: the glyph's tier has no Game:Items:ImprintCost", packet.RequestId);
            return result;
        }

        if (result != ItemImprintResult.Ok)
            return result;

        // Both were decided a moment ago, on this thread, against this state.
        if (cost > 0 && wallet.TrySpend(cost) != WalletResult.Ok)
            throw new InvalidOperationException($"Spending {cost} gold to imprint failed after ImprintRules allowed it.");

        return economy.InventoryOf(character).TryImprint(packet.GlyphBagSlot, packet.TargetBagSlot)
               ?? throw new InvalidOperationException("Imprinting failed after ImprintRules allowed it.");
    }
}
