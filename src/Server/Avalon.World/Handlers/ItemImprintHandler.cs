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
/// inventory service, which may brick the item (answered Bricked, a success: the gold and the glyph are spent). An
/// imprint the service refuses or throws on after the rules allowed it has written nothing (TryImprint throws only before
/// its first write), so it gets its gold back through IWalletRestore.GiveBack, which neither the gold cap nor a trade's
/// incoming gold can refuse (#1233), and is answered InternalError, logged at Error. The item, the emptied glyph
/// slot and the gold reach the client in the tick's SMSG_INVENTORY_UPDATE. Nothing is held open between requests: each
/// one re-checks the Workbench and its range. The item's instability is never logged.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_ITEM_IMPRINT)]
public sealed partial class ItemImprintHandler(ILogger<ItemImprintHandler> logger, IWorld world, ICharacterEconomy economy)
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
            // The request still gets its one reply, and nothing escapes onto the tick. The imprint's own throws are caught
            // and refunded below; what reaches here threw before any gold was spent.
            Log.Threw(logger, e, packet.RequestId);
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
            Log.NoCost(logger, packet.RequestId);
            return result;
        }

        if (result != ItemImprintResult.Ok)
            return result;

        // Both were decided a moment ago, on this thread, against this state.
        if (cost > 0 && wallet.TrySpend(cost) != WalletResult.Ok)
            throw new InvalidOperationException($"Spending {cost} gold to imprint failed after ImprintRules allowed it.");

        // A null or a throw has written nothing (TryImprint throws only before its first write), so either gives the gold
        // back: a failed imprint neither loses value nor makes it. Only then: an imprint that wrote anything keeps its price.
        ItemImprintResult? imprinted;
        try
        {
            imprinted = economy.InventoryOf(character).TryImprint(packet.GlyphBagSlot, packet.TargetBagSlot);
        }
        catch (Exception e)
        {
            Log.ImprintThrew(logger, e, packet.RequestId, cost, Refund(wallet, cost, character));
            return ItemImprintResult.InternalError;
        }

        if (imprinted is { } done)
            return done;

        Log.RefusedAfterAllowed(logger, packet.RequestId, cost, Refund(wallet, cost, character));
        return ItemImprintResult.InternalError;
    }

    /// <summary>
    /// Gives back the gold spent above. GiveBack, not TryAddGold (#1233): it restores the balance the character held before
    /// the spend, on this tick, so the cap or a trade's incoming gold can never refuse it. A wallet that cannot restore
    /// (not the economy's own) gets the gold back only within the cap, so no gold is made past it, logged at Error.
    /// </summary>
    private WalletResult Refund(IWallet wallet, ulong cost, CharacterEntity character)
    {
        if (cost == 0)
            return WalletResult.Ok;

        if (wallet is IWalletRestore restore)
        {
            restore.GiveBack(cost);
            return WalletResult.Ok;
        }

        WalletResult added = wallet.TryAddGold(cost);
        Log.GaveBackWithinTheCap(logger, cost, character.Guid.Id, added);
        return added;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "CMSG_ITEM_IMPRINT {RequestId} threw; answering InternalError")]
        public static partial void Threw(ILogger logger, Exception exception, uint requestId);

        [LoggerMessage(Level = LogLevel.Error, Message = "CMSG_ITEM_IMPRINT {RequestId}: the glyph's tier has no Game:Items:ImprintCost")]
        public static partial void NoCost(ILogger logger, uint requestId);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "CMSG_ITEM_IMPRINT {RequestId}: the inventory service refused the imprint ImprintRules allowed; " +
                      "answering InternalError, its {Cost} gold given back: {Refund}")]
        public static partial void RefusedAfterAllowed(ILogger logger, uint requestId, ulong cost, WalletResult refund);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "CMSG_ITEM_IMPRINT {RequestId}: the imprint ImprintRules allowed threw before writing anything; " +
                      "answering InternalError, its {Cost} gold given back: {Refund}")]
        public static partial void ImprintThrew(ILogger logger, Exception exception, uint requestId, ulong cost,
            WalletResult refund);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Giving back {Cost} gold after a failed imprint to character {CharacterId}: the wallet cannot " +
                      "restore past the cap, so it was added within it ({Result})")]
        public static partial void GaveBackWithinTheCap(ILogger logger, ulong cost, uint characterId, WalletResult result);
    }
}
