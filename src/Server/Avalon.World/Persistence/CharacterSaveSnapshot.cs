using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;

namespace Avalon.World.Persistence;

/// <summary>
/// An immutable picture of what one save writes, taken on the tick thread: a copy of the row, the
/// current values of every non-Unchanged item and slot, and the marks to acknowledge once it commits.
/// </summary>
/// <remarks>
/// The marks are dictionaries keyed by item id and by (container, slot), so the batch carries each
/// item and each slot at most once, as <see cref="ICharacterSaveRepository.WriteAsync" /> requires.
/// </remarks>
public sealed record CharacterSaveSnapshot(CharacterSaveBatch Batch, SaveMarks Marks)
{
    private static readonly InventoryType[] Containers = [InventoryType.Equipment, InventoryType.Bag, InventoryType.Bank];

    public CharacterId CharacterId => Batch.Row.Id;

    public static CharacterSaveSnapshot Take(CharacterEntity character)
    {
        Character row = character.Data
            ?? throw new InvalidOperationException("A character without a row has nothing to save.");
        SaveMarks marks = character.SaveState.TakeMarks();
        DateTime now = DateTime.UtcNow;

        Dictionary<ItemInstanceId, InventoryItem> held = [];
        foreach (InventoryType container in Containers)
        {
            foreach (InventoryItem item in character.Container(container).Items)
                held[item.InstanceId] = item;
        }

        List<ItemInstance> upsertItems = [];
        List<ItemInstanceId> deleteItems = [];
        foreach ((ItemInstanceId id, SaveMark mark) in marks.Items)
        {
            // Memory is authoritative: an item no longer held is deleted, whatever its mark says.
            if (mark.State != SaveState.Removed && held.TryGetValue(id, out InventoryItem item))
            {
                upsertItems.Add(new ItemInstance
                {
                    Id = item.InstanceId,
                    TemplateId = item.TemplateId,
                    CharacterId = row.Id,
                    Count = item.Count,
                    Durability = item.Durability,
                    Charges = item.Charges,
                    Flags = item.Flags,
                    UpdatedAt = now,
                });
            }
            else
            {
                deleteItems.Add(id);
            }
        }

        List<CharacterInventory> upsertSlots = [];
        List<(InventoryType Container, ushort Slot)> deleteSlots = [];
        foreach (((InventoryType Container, ushort Slot) key, SaveMark mark) in marks.Slots)
        {
            if (mark.State != SaveState.Removed &&
                character.Container(key.Container).TryGet(key.Slot, out InventoryItem item))
            {
                upsertSlots.Add(new CharacterInventory
                {
                    CharacterId = row.Id, Container = key.Container, Slot = key.Slot, ItemId = item.InstanceId,
                });
            }
            else
            {
                deleteSlots.Add(key);
            }
        }

        CharacterStats? stats = marks.StatsVersion is not null && character.Stats is { } derived
            ? derived.ToRow(row.Id)
            : null;

        CharacterQuestWrite? quests = null;
        if (marks.Quests is { Count: > 0 } questMarks)
        {
            List<uint> rewrite = [];
            List<CharacterQuest> active = [];
            List<CharacterQuestObjective> objectives = [];
            List<CharacterCompletedQuest> completed = [];
            foreach (uint questId in questMarks.Keys.Order())
            {
                // Memory is authoritative: whatever the quest is now is what the rows become.
                rewrite.Add(questId);
                if (character.Quests.Get(questId) is { } quest)
                {
                    active.Add(new CharacterQuest
                    {
                        CharacterId = row.Id, QuestId = questId, State = quest.State, Stage = quest.Stage, AcceptedAt = quest.AcceptedAt,
                    });
                    objectives.AddRange(quest.Progress.Select(p => new CharacterQuestObjective
                        { CharacterId = row.Id, QuestId = questId, ObjectiveId = p.Key, Progress = p.Value }));
                }
                else if (character.Quests.CompletedAt(questId) is { } at)
                {
                    completed.Add(new CharacterCompletedQuest { CharacterId = row.Id, QuestId = questId, CompletedAt = at });
                }
            }

            quests = new CharacterQuestWrite(rewrite, active, objectives, completed);
        }

        return new CharacterSaveSnapshot(
            new CharacterSaveBatch(row.Copy(), upsertItems, deleteItems, upsertSlots, deleteSlots, stats, quests,
                IgnoresOf(character, row.Id, marks)),
            marks);
    }

    /// <summary>
    /// #723: every marked ignore entry is rewritten from memory: deleted, and inserted again while still on the list.
    /// </summary>
    private static CharacterIgnoreWrite? IgnoresOf(CharacterEntity character, CharacterId owner, SaveMarks marks)
    {
        if (marks.Ignores is not { Count: > 0 } ignoreMarks)
            return null;

        List<CharacterId> rewrite = [];
        List<CharacterIgnore> insert = [];
        foreach (uint ignoredId in ignoreMarks.Keys.Order())
        {
            rewrite.Add(ignoredId);
            if (character.Ignores.Entries.FirstOrDefault(e => e.Id == ignoredId) is { } entry)
            {
                insert.Add(new CharacterIgnore
                {
                    CharacterId = owner, IgnoredCharacterId = ignoredId, CreatedAt = entry.CreatedAt,
                });
            }
        }

        return new CharacterIgnoreWrite(rewrite, insert);
    }
}
