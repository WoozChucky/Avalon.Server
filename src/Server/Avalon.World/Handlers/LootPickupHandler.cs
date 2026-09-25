using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Loot;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Public;
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
    TimeProvider time) : WorldPacketHandler<CLootPickupPacket>
{
    public override void Execute(IWorldConnection connection, CLootPickupPacket packet)
    {
        // 1. Alive. No result code exists for this, and every in-map handler drops a dead
        // character's request without a reply.
        if (connection.Character is not CharacterEntity character || character.IsDead)
        {
            logger.LogDebug("Dropped CMSG_LOOT_PICKUP from a dead or missing character");
            return;
        }

        var lootGuid = new ObjectGuid(packet.LootGuid);
        var host = world.InstanceRegistry.GetInstanceById(character.InstanceId) as IGroundLootHost;

        LootPickupOutcome outcome = LootPickup.TryPickUp(
            character, host?.Drops, lootGuid, world.Configuration.LootPickupRange,
            time.GetUtcNow().UtcDateTime, economy, logger);

        connection.Send(SLootPickupResultPacket.Create(packet.LootGuid, outcome.Result, connection.CryptoSession.Encrypt));

        if (outcome.Removed)
        {
            host!.BroadcastLootDespawned([lootGuid]);
        }
    }
}
