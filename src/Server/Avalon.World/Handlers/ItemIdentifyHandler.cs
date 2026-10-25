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
/// Identifies one Bag item at a Resonance Workbench (#1179), on the tick, in the map pass. Every request gets exactly
/// one SMSG_ITEM_IDENTIFY_RESULT; a connection with no character gets nothing. IdentifyRules decides; an Ok pays
/// Game:Items:IdentifyCost through the wallet (nothing when it is 0) and then identifies the item through the inventory
/// service, so the revealed item, with its name and every modifier, and the gold reach the client in the tick's
/// SMSG_INVENTORY_UPDATE. Nothing is held open between requests: each one re-checks the Workbench and its range.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_ITEM_IDENTIFY)]
public sealed class ItemIdentifyHandler(ILogger<ItemIdentifyHandler> logger, IWorld world, ICharacterEconomy economy)
    : WorldPacketHandler<CItemIdentifyPacket>
{
    public override void Execute(IWorldConnection connection, CItemIdentifyPacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_ITEM_IDENTIFY from a connection with no character");
            return;
        }

        ItemIdentifyResult result;
        try
        {
            result = Identify(character, packet);
        }
        catch (Exception e)
        {
            // The request still gets its one reply, and nothing escapes onto the tick. The gold is taken before the item
            // is identified, so a failure caused by a bug loses value, never creates it, and is not rolled back.
            logger.LogError(e, "CMSG_ITEM_IDENTIFY {RequestId} for Bag slot {BagSlot} threw; answering NotFound",
                packet.RequestId, packet.BagSlot);
            result = ItemIdentifyResult.NotFound;
        }

        connection.Send(SItemIdentifyResultPacket.Create(packet.RequestId, result, PacketEncoder.Shared));
    }

    private ItemIdentifyResult Identify(CharacterEntity character, CItemIdentifyPacket packet)
    {
        var host = world.InstanceRegistry.GetInstanceById(character.InstanceId) as IDoodadHost;
        ulong cost = world.Configuration.Items.IdentifyCost;
        // A wallet is made per call, so it is taken only when there is gold to ask for: at no cost (v1) a request,
        // refused or not, allocates nothing.
        IWallet? wallet = cost > 0 ? economy.WalletOf(character) : null;

        ItemIdentifyResult result = IdentifyRules.Decide(character, host, packet.WorkbenchGuid, packet.BagSlot, cost,
            wallet);
        if (result != ItemIdentifyResult.Ok)
            return result;

        // Both were decided a moment ago, on this thread, against this state.
        if (wallet is not null && wallet.TrySpend(cost) != WalletResult.Ok)
            throw new InvalidOperationException($"Spending {cost} gold to identify failed after IdentifyRules allowed it.");

        if (!economy.InventoryOf(character).TryIdentify(packet.BagSlot))
            throw new InvalidOperationException($"Identifying Bag slot {packet.BagSlot} failed after IdentifyRules allowed it.");

        return ItemIdentifyResult.Ok;
    }
}
