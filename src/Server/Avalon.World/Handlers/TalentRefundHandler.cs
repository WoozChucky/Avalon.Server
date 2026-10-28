using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Talents;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Talents;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Refunds talent nodes at the Lectern of Paths (#1181 Part C), all or nothing, on the tick, in the map pass. Every
/// request gets exactly one SMSG_TALENT_RESULT; a connection with no character gets nothing. TalentRefundRules decides
/// against the Lectern, its range, the cast, every id and the gold before anything changes, so a refusal changes nothing,
/// marks no save, spends no gold and allocates nothing beyond its pooled reply. An accepted request pays the price
/// through the wallet (nothing up to the free level), then holds the nodes no more, then TalentChange refreshes the stats
/// and the abilities and marks the talent state owed, and the ability list when the abilities held changed; the tick's
/// TalentFlusher sends both after the result. The gold and the refunded rows are marked on the same tick, so the next
/// save writes both in one transaction. Nothing is held open between requests: each one re-checks the Lectern and its
/// range.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_TALENT_REFUND)]
public sealed class TalentRefundHandler(ILogger<TalentRefundHandler> logger, IWorld world, ICharacterEconomy economy)
    : WorldPacketHandler<CTalentRefundPacket>
{
    public override void Execute(IWorldConnection connection, CTalentRefundPacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_TALENT_REFUND from a connection with no character");
            return;
        }

        TalentResult result;
        uint refusedNode;
        try
        {
            result = Refund(character, packet, out refusedNode);
        }
        catch (Exception e)
        {
            // The request still gets its one reply, and nothing escapes onto the tick. The rules decided before anything
            // changed; a throw before the nodes were dropped gave the gold back, and one after leaves the refund done and
            // paid for, with the state owed, so the window learns of it.
            logger.LogError(e, "CMSG_TALENT_REFUND {RequestId} of {Count} nodes threw; answering InternalError",
                packet.RequestId, packet.NodeIds.Count);
            result = TalentResult.InternalError;
            refusedNode = 0;
        }

        connection.Send(STalentResultPacket.Create(packet.RequestId, result, refusedNode, PacketEncoder.Shared));
    }

    private TalentResult Refund(CharacterEntity character, CTalentRefundPacket packet, out uint refusedNode)
    {
        // One read: the tree, the abilities and the stats the change rebuilds are one generation.
        StaticData data = world.Data;
        var host = world.InstanceRegistry.GetInstanceById(character.InstanceId) as IDoodadHost;
        ulong price = TalentPrice.Total(TalentPrice.PerNode(character.Level, world.Configuration.Talents),
            packet.NodeIds.Count);
        // Taken only when there is gold to ask for; it is the character's own, kept by the economy, so a request at any
        // price, refused or not, allocates nothing.
        IWallet? wallet = price > 0 ? economy.WalletOf(character) : null;

        TalentResult result = TalentRefundRules.Decide(character, host, packet.LecternGuid, data.Talents,
            packet.NodeIds, price, wallet, out refusedNode);
        if (result != TalentResult.Ok || packet.NodeIds.Count == 0)
            return result;

        // Both were decided a moment ago, on this thread, against this state.
        if (wallet is not null && wallet.TrySpend(price) != WalletResult.Ok)
            throw new InvalidOperationException($"Spending {price} gold on a talent refund failed after the rules allowed it.");

        try
        {
            character.Talents.Refund(packet.NodeIds);
        }
        catch
        {
            // Dropping the nodes checks every id before it changes anything, so a throw left them all held: nothing
            // changed, and the gold goes back.
            if (wallet is not null && wallet.TryAddGold(price) != WalletResult.Ok)
            {
                logger.LogError("Giving back {Price} gold after a failed talent refund to character {CharacterId} failed",
                    price, character.Guid.Id);
            }

            throw;
        }

        TalentChange.Apply(character, data, logger);
        return TalentResult.Ok;
    }
}
