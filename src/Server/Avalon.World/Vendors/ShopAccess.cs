using System.Diagnostics.CodeAnalysis;
using Avalon.Common;
using Avalon.World.Dialogue;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;

namespace Avalon.World.Vendors;

/// <summary>When a character may use a shop (spec #432), mirroring BankAccess. Tick thread only.</summary>
public static class ShopAccess
{
    /// <summary>
    /// The shop window is open: the conversation the shop was opened in is still the one open. No
    /// lookups, so the vendor pass asks it of every connection every tick.
    /// </summary>
    public static bool IsOpen(IWorldConnection connection, CharacterEntity character) =>
        character.OpenShopNpc is { } vendor
        && connection.CurrentDialogue is { } open
        && open.Npc == vendor;

    /// <summary>
    /// Checked on every vendor request. The shop is open, the instance keeps stock, and the vendor
    /// is still there, alive, still a vendor, and inside the dialogue leash. When the shop is open
    /// but any of the rest fails, the conversation is ended, as NpcInteraction.IsWithinLeash
    /// requires of every handler acting on an open conversation. On success,
    /// <paramref name="stock" /> is the vendor's live stock, reconciled with the current catalog.
    /// </summary>
    public static bool TryUse(
        IWorldConnection connection,
        CharacterEntity character,
        IWorld world,
        [NotNullWhen(true)] out VendorStockState? stock)
    {
        stock = null;
        if (!IsOpen(connection, character))
            return false;

        ObjectGuid vendor = character.OpenShopNpc!;
        ISimulationContext? context = world.InstanceRegistry.GetInstanceById(character.InstanceId);

        if (context is not IVendorHost host
            || !context.Creatures.TryGetValue(vendor, out ICreature? npc)
            || npc.CurrentHealth == 0
            || !NpcInteraction.IsVendor(world.Data.DialogueActions, npc.Metadata.Id)
            || !NpcInteraction.IsWithinLeash(character.Position, npc.Position))
        {
            NpcInteraction.EndConversation(connection, vendor);
            return false;
        }

        stock = host.Vendors.For(vendor, npc.Metadata.Id, world.Data.Vendors.RowsFor(npc.Metadata.Id));
        return true;
    }
}
