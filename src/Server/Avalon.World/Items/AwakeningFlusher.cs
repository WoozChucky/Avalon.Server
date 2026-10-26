using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Items;

/// <summary>
/// The tick's Awakening stage (#1180), run by WorldServer per connection after both passes and before the quest and
/// inventory flushes. For a character whose meter is due it settles the pending points (<see cref="Awakening.Settle" />),
/// wakes every worn item whose sealed affix reached its threshold (<see cref="IInventoryService.TryAwaken" />), sends one
/// SItemAwakenedPacket per wake and refreshes the stats when anything woke; for one whose progress moved, it sends one
/// SAwakeningProgressPacket listing every worn sealed affix. This is the only place a seal wakes, and only a worn one: a
/// settle never wakes, so a wake is sent once, from here. A character with neither due nor unsent costs two bool reads;
/// the rest allocates nothing past the packets, its slots on the stack and its progress entries reused. One per
/// WorldServer. Tick thread only.
/// </summary>
public sealed class AwakeningFlusher(ICharacterEconomy economy, ILogger logger)
{
    // The progress packet's entries, reused by every flush: the packet is encoded as it is made, so the next flush may
    // overwrite them. _entries holds every entry ever made; _sent is the packet's list, refilled from them.
    private readonly List<AwakeningProgressDto> _entries = [];
    private readonly List<AwakeningProgressDto> _sent = [];

    public void Flush(IWorldConnection connection, StaticData data)
    {
        if (connection.Character is not CharacterEntity { Data: not null } character)
            return;

        AwakeningMeter meter = character.Awakening;
        if (meter.Due)
        {
            // Cleared after the settle, which leaves the meter due when it moved progress: the look for a full bar it
            // asks for is the one that follows. A refresh after a wake that still finds a seal worn makes it due again.
            Awakening.Settle(character);
            meter.Due = false;
            Wake(connection, character, data);
        }

        if (meter.Unsent)
        {
            meter.Unsent = false;
            SendProgress(connection, character);
        }
    }

    /// <summary>
    /// Wakes each worn item with a full bar. The slots are found first, so the Equipment is not walked while a wake
    /// changes it; a refresh follows when anything woke, which counts Resonance again since the item's list is new.
    /// </summary>
    private void Wake(IWorldConnection connection, CharacterEntity character, StaticData data)
    {
        CharacterInventoryContainer equipment = character.Container(InventoryType.Equipment);
        Span<byte> ready = stackalloc byte[equipment.Capacity];
        int count = 0;
        foreach (InventoryItem item in equipment.Held)
        {
            if (HasReadyRow(character, item))
                ready[count++] = item.Slot;
        }

        if (count == 0)
            return;

        IInventoryService inventory = economy.InventoryOf(character);
        bool woke = false;
        for (int i = 0; i < count; i++)
        {
            if (inventory.TryAwaken(ready[i]) is not { } awakened)
                continue;

            connection.Send(SItemAwakenedPacket.Create(awakened.EquipmentSlot, (uint)awakened.ModifierId, awakened.Outcome,
                PacketEncoder.Shared));
            woke = true;
        }

        if (woke)
            CharacterStatsRefresh.AfterGearChange(character, data, logger);
    }

    private static bool HasReadyRow(CharacterEntity character, in InventoryItem item)
    {
        IReadOnlyList<ItemInstanceModifier> rows = character.ItemModifiers.Of(item.InstanceId);
        for (int i = 0; i < rows.Count; i++)
        {
            if (Awakening.ReadyToWake(item, rows[i]))
                return true;
        }

        return false;
    }

    /// <summary>Every worn row that fills, as (slot, progress) in Equipment slot order; nothing is sent for none.</summary>
    private void SendProgress(IWorldConnection connection, CharacterEntity character)
    {
        _sent.Clear();
        CharacterInventoryContainer equipment = character.Container(InventoryType.Equipment);
        for (byte slot = 0; slot < equipment.Capacity; slot++)
        {
            if (!equipment.TryGet(slot, out InventoryItem item))
                continue;

            IReadOnlyList<ItemInstanceModifier> rows = character.ItemModifiers.Of(item.InstanceId);
            for (int i = 0; i < rows.Count; i++)
            {
                if (!Awakening.Fills(item, rows[i]))
                    continue;

                if (_sent.Count == _entries.Count)
                    _entries.Add(new AwakeningProgressDto());

                AwakeningProgressDto entry = _entries[_sent.Count];
                entry.EquipmentSlot = slot;
                entry.Progress = (uint)rows[i].SealProgress.GetValueOrDefault();
                _sent.Add(entry);
            }
        }

        if (_sent.Count > 0)
            connection.Send(SAwakeningProgressPacket.Create(_sent, PacketEncoder.Shared));

        _sent.Clear();
    }
}
