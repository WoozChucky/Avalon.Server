using System.Diagnostics;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Loot;
using Avalon.World.Entities;
using Avalon.World.Inventory;

namespace Avalon.World.Loot;

/// <summary>
/// Whether a character may take a drop, and taking it. Tick thread: it changes the character's
/// containers, money, save marks and client changes.
/// </summary>
public static class LootPickup
{
    /// <summary>
    /// The pickup checks after "the character is alive", which the handler makes, in this order.
    /// The first failure answers; anything but Ok leaves the drop where it is, except a drop whose
    /// item template no longer exists, which nobody could ever take.
    /// </summary>
    /// <param name="store">The drops in the character's own instance; null when it has none.</param>
    /// <param name="now">UTC, from the same clock the allocator stamped FreeForAllAt with.</param>
    public static LootPickupOutcome TryPickUp(
        CharacterEntity character,
        GroundLootStore? store,
        ObjectGuid lootGuid,
        float pickupRange,
        DateTime now,
        ICharacterEconomy economy)
    {
        // 2. In the character's own instance.
        if (store is null || !store.TryGet(lootGuid, out GroundLoot? drop))
            return new LootPickupOutcome(LootPickupResult.NotFound, false);

        // 3. Close enough. The same 3D distance the NPC interact check uses.
        if (Vector3.Distance(character.Position, drop.Position) > pickupRange)
            return new LootPickupOutcome(LootPickupResult.TooFar, false);

        // 4. Theirs, or anyone's by now.
        if (drop.OwnerCharacterId != character.Guid.Id && now < drop.FreeForAllAt)
            return new LootPickupOutcome(LootPickupResult.NotYours, false);

        if (drop.ItemTemplateId is { } itemId)
        {
            // 5. All or nothing: a refused add changes nothing.
            switch (economy.InventoryOf(character).TryAdd(itemId, drop.Count))
            {
                case InventoryAddResult.Ok:
                    break;
                case InventoryAddResult.InventoryFull:
                    return new LootPickupOutcome(LootPickupResult.InventoryFull, false);
                case InventoryAddResult.UniqueAlreadyOwned:
                    return new LootPickupOutcome(LootPickupResult.UniqueAlreadyOwned, false);
                case InventoryAddResult.UnknownTemplate:
                    // An Items reload removed the template after this drop was rolled. Nobody can
                    // take it, so it leaves the ground instead of refusing every click forever.
                    store.Remove(lootGuid);
                    return new LootPickupOutcome(LootPickupResult.NotFound, true);
                default:
                    throw new UnreachableException("Unhandled InventoryAddResult");
            }
        }
        else if (economy.WalletOf(character).TryAddMoney(drop.Gold) != WalletResult.Ok)
        {
            // 6. TryAddMoney refuses only past the cap.
            return new LootPickupOutcome(LootPickupResult.MoneyCapReached, false);
        }

        // 7. Taken.
        store.Remove(lootGuid);
        return new LootPickupOutcome(LootPickupResult.Ok, true);
    }
}
