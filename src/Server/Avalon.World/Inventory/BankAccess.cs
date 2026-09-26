using Avalon.Common;
using Avalon.Network.Packets.Character;
using Avalon.World.Dialogue;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;

namespace Avalon.World.Inventory;

/// <summary>When a character may use its bank (spec #463). Tick thread only.</summary>
public static class BankAccess
{
    /// <summary>
    /// The bank window is open: the conversation the bank was opened in is still the one open.
    /// No lookups, so the flusher asks it of every connection every tick.
    /// </summary>
    public static bool IsOpen(IWorldConnection connection, CharacterEntity character) =>
        character.OpenBankNpc is { } banker
        && connection.CurrentDialogue is { } open
        && open.Npc == banker;

    /// <summary>
    /// For a request that touches the Bank, checked on every such request: the bank is open, and
    /// its NPC is still in the character's instance, alive, still a banker, and inside the dialogue
    /// leash. When the bank is open but any of the rest fails, the conversation is ended, as
    /// NpcInteraction.IsWithinLeash requires of every handler acting on an open conversation.
    /// </summary>
    public static bool TryUse(IWorldConnection connection, CharacterEntity character, IWorld world)
    {
        if (!IsOpen(connection, character))
            return false;

        ObjectGuid banker = character.OpenBankNpc!;
        ISimulationContext? context = world.InstanceRegistry.GetInstanceById(character.InstanceId);

        if (context is null
            || !context.Creatures.TryGetValue(banker, out ICreature? npc)
            || npc.CurrentHealth == 0
            || !NpcInteraction.IsBanker(world.Data.DialogueActions, npc.Metadata.Id)
            || !NpcInteraction.IsWithinLeash(character.Position, npc.Position))
        {
            NpcInteraction.EndConversation(connection, banker);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Every Bank slot, as absolute entries (an empty slot has no item), in slot order. Sent once
    /// when the bank opens, the first time Bank slots reach the client.
    /// </summary>
    public static InventorySlotUpdateDto[] Snapshot(CharacterEntity character)
    {
        CharacterInventoryContainer bank = character.Container(InventoryType.Bank);
        var slots = new InventorySlotUpdateDto[bank.Capacity];

        for (ushort slot = 0; slot < bank.Capacity; slot++)
        {
            slots[slot] = new InventorySlotUpdateDto
            {
                Container = (ushort)InventoryType.Bank,
                Slot = slot,
                Item = bank.TryGet(slot, out InventoryItem item) ? ItemSlotDtoMapper.ToDto(InventoryType.Bank, item) : null,
            };
        }

        return slots;
    }
}
