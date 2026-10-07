using Avalon.Common;
using Avalon.Common.GameAuth;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.World.Auras;
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

    public static CharacterSaveSnapshot Take(CharacterEntity character, GameplayWriteAuthority? connectionAuthority = null)
    {
        Character row = character.Data
            ?? throw new InvalidOperationException("A character without a row has nothing to save.");
        var authority = character.GameplayAuthority;
        if (connectionAuthority is not null && (authority is null || authority.AccountId != connectionAuthority.AccountId ||
            authority.GameSessionId != connectionAuthority.GameSessionId || authority.FencingToken != connectionAuthority.FencingToken))
            throw new InvalidOperationException("A save cannot be relabeled by a replacement connection.");
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
                    CharacterId = row.Id,
                    Container = key.Container,
                    Slot = key.Slot,
                    ItemId = item.InstanceId,
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
                        CharacterId = row.Id,
                        QuestId = questId,
                        State = quest.State,
                        Stage = quest.Stage,
                        AcceptedAt = quest.AcceptedAt,
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
                IgnoresOf(character, row.Id, marks), AurasOf(character, row.Id, marks))
            { Authority = authority },
            marks);
    }

    /// <summary>
    /// Every save of a character holding an aura rewrites them all, with the time each has left on the character's
    /// clock (or, while restored auras wait for the character to enter the world, at the moment their time stopped) and
    /// the fraction of a point its ticks carry, so a save is never behind; a save marked by a change, with none left,
    /// deletes them. A dead character holds none, whatever memory says: death ends every aura. Nothing otherwise.
    /// </summary>
    private static CharacterAuraWrite? AurasOf(CharacterEntity character, CharacterId owner, SaveMarks marks)
    {
        if (character.IsDead)
            return new CharacterAuraWrite([]);

        if (marks.AurasVersion is null && character.Auras.Count == 0)
            return null;

        DateTimeOffset now = character.Auras.HeldSince ?? character.Clock.GetUtcNow();
        List<CharacterAura> rows = [];
        foreach (ActiveAura aura in character.Auras.All)
        {
            uint remaining = UnitAuras.RemainingMs(aura, now);
            if (remaining == 0 && aura.Schedule.TicksLeft <= 0)
                continue;   // over, with nothing left to deal: the next pass ends it

            rows.Add(new CharacterAura
            {
                CharacterId = owner,
                Slot = rows.Count,
                AuraId = aura.Id.Value,
                CasterGuid = SavedCaster(aura.CasterGuid),
                SourceAbilityId = aura.Source.AbilityId?.Value,
                Stacks = (int)aura.Stacks,
                RemainingMs = remaining,
                DurationMs = aura.DurationMs,
                TicksLeft = aura.Schedule.TicksLeft,
                TickAmount = aura.Snapshot.PerTickPerStack,
                CritPct = aura.Snapshot.CritPct,
                CasterLevel = aura.Snapshot.CasterLevel,
                PeriodicCarry = aura.PeriodicCarry,
                AppliedAt = aura.AppliedAt,
            });
        }

        return new CharacterAuraWrite(rows);
    }

    /// <summary>
    /// A character's guid is built from its id, so it names the same character after a restart; any other unit's guid
    /// is numbered per run and could name an unrelated one then, so it is saved as nobody.
    /// </summary>
    private static ulong SavedCaster(ObjectGuid caster) => caster.Type == ObjectType.Character ? caster.RawValue : 0UL;

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
                    CharacterId = owner,
                    IgnoredCharacterId = ignoredId,
                    CreatedAt = entry.CreatedAt,
                });
            }
        }

        return new CharacterIgnoreWrite(rewrite, insert);
    }
}
