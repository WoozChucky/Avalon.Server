using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Destroys some or all of one stack (spec #463), on the tick, in the map pass. Answered with
/// exactly one SMSG_ITEM_RESULT; a destroyed worn item refreshes the stats.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_ITEM_DESTROY)]
public class ItemDestroyHandler(ILogger<ItemDestroyHandler> logger, IWorld world, ICharacterEconomy economy)
    : WorldPacketHandler<CItemDestroyPacket>
{
    // A refusal's modifier entries (#1179), given back once the reply is encoded. Handlers run on the tick, one at a time.
    private readonly ItemModifierDtoPool _modifierDtos = new();

    public override void Execute(IWorldConnection connection, CItemDestroyPacket packet)
    {
        if (connection.Character is not CharacterEntity character)
        {
            logger.LogDebug("Dropped CMSG_ITEM_DESTROY from a connection with no character");
            return;
        }

        bool read = SlotRef.TryParse(packet.Container, packet.Slot, out SlotRef slot);

        ItemRequestResult result;
        try
        {
            result = Destroy(connection, character, read, slot, packet.Count);
        }
        catch (Exception e)
        {
            logger.LogError(e, "CMSG_ITEM_DESTROY {RequestId} of {Slot} threw; answering NotFound",
                packet.RequestId, slot);
            result = ItemRequestResult.NotFound;
        }

        // The slot a refusal reports, read where it stands: a request allocates nothing to name it (#883).
        ItemRequestReply.Send(connection, character, packet.RequestId, result,
            read ? new ReadOnlySpan<SlotRef>(in slot) : default, world.Data.Items, _modifierDtos);
    }

    private ItemRequestResult Destroy(
        IWorldConnection connection, CharacterEntity character, bool readable, SlotRef slot, uint? count)
    {
        if (character.IsDead)
            return ItemRequestResult.Dead;

        if (!readable)
            return ItemRequestResult.InvalidSlot;

        // #770: a stunned character takes no gear off, destroyed or not, and acts on no open bank; Bag items still go.
        if ((slot.Container is InventoryType.Bank or InventoryType.Equipment) && !ControlGates.CanAct(character))
            return ItemRequestResult.Stunned;

        bool bankAccessible = slot.Container == InventoryType.Bank && BankAccess.TryUse(connection, character, world);

        ItemRequestResult result = economy.InventoryOf(character).TryDestroy(slot, count, bankAccessible);

        if (result == ItemRequestResult.Ok && slot.Container == InventoryType.Equipment)
            CharacterStatsRefresh.AfterGearChange(character, world.Data, logger);

        return result;
    }
}
