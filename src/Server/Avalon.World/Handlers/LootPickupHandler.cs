using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Loot;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Picks up one drop (issue #460). Runs on the tick, in the map pass, like every in-map handler.
/// The picker hears the result; everyone in the instance hears the despawn; the picker's inventory
/// update leaves at the end of the tick through InventoryUpdateFlusher.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_LOOT_PICKUP)]
public class LootPickupHandler(
    ILogger<LootPickupHandler> logger,
    IWorld world,
    ICharacterEconomy economy,
    TimeProvider time,
    QuestService? quests = null) : WorldPacketHandler<CLootPickupPacket>
{
    // The one drop a pickup despawns, kept for every pickup (#973): the handler is built once, runs on the tick only,
    // and the despawn packet copies the guids before it is encoded, so nothing holds this array past the call.
    private readonly ObjectGuid[] _despawned = new ObjectGuid[1];

    public override void Execute(IWorldConnection connection, CLootPickupPacket packet)
    {
        // 1. Alive. No result code exists for this, and every in-map handler drops a dead
        // character's request without a reply.
        if (connection.Character is not CharacterEntity character || character.IsDead)
        {
            logger.LogDebug("Dropped CMSG_LOOT_PICKUP from a dead or missing character");
            return;
        }

        // 2. Free to act (#770): a stunned character picks nothing up, and hears so.
        if (!ControlGates.CanAct(character))
        {
            connection.Send(SLootPickupResultPacket.Create(packet.LootGuid, LootPickupResult.Stunned, PacketEncoder.Shared));
            return;
        }

        var lootGuid = new ObjectGuid(packet.LootGuid);
        var host = world.InstanceRegistry.GetInstanceById(character.InstanceId) as IGroundLootHost;

        LootPickupOutcome outcome = LootPickup.TryPickUp(
            character, host?.Drops, lootGuid, world.Configuration.LootPickupRange,
            time.GetUtcNow().UtcDateTime, economy, logger, quests);

        connection.Send(SLootPickupResultPacket.Create(packet.LootGuid, outcome.Result, PacketEncoder.Shared));

        if (outcome.Removed)
        {
            _despawned[0] = lootGuid;
            host!.BroadcastLootDespawned(_despawned);
        }
    }
}
