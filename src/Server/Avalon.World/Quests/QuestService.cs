using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Quest;
using Avalon.World.Dialogue;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Localization;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Quests;

/// <summary>
/// Everything a quest does at run time (#433): availability, the dialogue options and offers, accept, progress
/// (kills, talk, collect, scripts), stages, turn-in and abandon. A DI singleton, World-side, and <b>tick thread
/// only</b>: every caller (the quest handlers, InteractHandler, DialogueChooseHandler, MapInstance.CreatureKilled,
/// QuestFlusher) runs on the tick. No timers and no awaits; the clock is read only to stamp accept and completion.
/// It changes a character only through its QuestLog, its IInventoryService and its IWallet.
/// </summary>
public sealed class QuestService(
    IWorld world,
    IServiceProvider services,
    ICharacterEconomy economy,
    ILootRandom random,
    TimeProvider time,
    ILogger<QuestService> logger,
    PartyService? parties = null)
{
    // The quests whose Settle loop is running, so a stage-start hook that advances an objective of the same quest
    // (Task 7's collect recount, Task 9's scripts) does not settle it from inside its own loop. Per quest, never
    // service-wide: a hook that advances another quest, or another character's, must settle that one at once.
    // Tick thread only.
    private readonly HashSet<ActiveQuest> _settling = [];

    /// <summary>
    /// Runs after a stage of an active quest starts (stage 0 at accept included), after the service's own
    /// stage-start work. World-side, never on the modding API. Its one use today is to let tests drive progress
    /// from inside a stage start, as the collect recount and quest scripts will.
    /// </summary>
    public Action<CharacterEntity, QuestView, ActiveQuest>? AfterStageStarted { get; set; }

    /// <summary>The generation of quests this tick reads. One reference per call site.</summary>
    public QuestCatalog Catalog => world.Data.Quests;

    public QuestResult Availability(CharacterEntity character, QuestView quest) =>
        QuestAvailability.Check(quest, character, world.Configuration.MaxActiveQuests, () => ScriptAllows(quest, character));

    public static QuestStageView? CurrentStage(QuestView quest, ActiveQuest active) =>
        active.Stage >= 0 && active.Stage < quest.Stages.Count ? quest.Stages[active.Stage] : null;

    /// <summary>
    /// CMSG_QUEST_ACCEPT (#433): an open conversation with the NPC inside the leash, the NPC is the quest's giver,
    /// and the quest is available. Starts at stage 0 with every count at 0.
    /// </summary>
    public QuestResult Accept(IWorldConnection connection, CharacterEntity character, uint questId, ulong npcGuid)
    {
        QuestResult talk = Conversation(connection, character, npcGuid, out ICreature? npc);
        if (talk != QuestResult.Ok)
            return talk;

        if (!Catalog.TryGet(questId, out QuestView? quest) || quest.GiverCreatureId.Value != npc!.Metadata.Id.Value)
            return QuestResult.NotAvailable;

        QuestResult available = Availability(character, quest);
        if (available != QuestResult.Ok)
            return available;

        ActiveQuest active = character.Quests.Start(questId, time.GetUtcNow().UtcDateTime);
        character.Quests.Say($"Quest accepted: {Title(character, quest)}.");
        OnAccepted(character, quest, active);
        StageStarted(character, quest, active);
        Settle(character, quest, active);
        AfterConversationChange(connection, character, npc);
        return QuestResult.Ok;
    }

    /// <summary>CMSG_QUEST_ABANDON (#433), anywhere: the quest, its progress and its quest items are gone; it can be accepted again.</summary>
    public QuestResult Abandon(CharacterEntity character, uint questId)
    {
        if (!character.Quests.IsActive(questId))
            return QuestResult.NotActive;

        // A quest a reload removed can still be dropped; it has no items the catalog could name.
        Catalog.TryGet(questId, out QuestView? quest);
        if (quest is not null)
            RemoveQuestItems(character, quest);

        character.Quests.Remove(questId);
        character.Quests.Say($"Quest abandoned: {(quest is null ? $"#{questId}" : Title(character, quest))}.");
        return QuestResult.Ok;
    }

    /// <summary>
    /// Adds to one objective of an active quest's current stage, capped at its count. False, changing nothing, for
    /// 0, a quest that is not active (or is ready), unknown to the catalog, or an objective outside the current stage.
    /// </summary>
    public bool AddProgress(CharacterEntity character, uint questId, uint objectiveId, uint amount)
    {
        if (amount == 0
            || character.Quests.Get(questId) is not { State: CharacterQuestState.Active } active
            || !Catalog.TryGet(questId, out QuestView? quest)
            || CurrentStage(quest, active)?.Objectives.FirstOrDefault(o => o.Id == objectiveId) is not { } objective)
            return false;

        ulong total = (ulong)active.ProgressOf(objectiveId) + amount;
        return SetCount(character, quest, active, objective, (uint)Math.Min(total, objective.Count));
    }

    /// <summary>Sets an objective's count (capped), says the milestone, and settles the stages.</summary>
    private bool SetCount(CharacterEntity character, QuestView quest, ActiveQuest active, QuestObjectiveView objective, uint value)
    {
        value = Math.Min(value, objective.Count);
        if (!character.Quests.SetProgress(active, objective.Id, value))
            return false;

        character.Quests.Say($"{Text(character, objective.DescriptionTextId)}: {value}/{objective.Count}");
        Settle(character, quest, active);
        return true;
    }

    /// <summary>
    /// While every objective of the current stage is met: the next stage starts, or after the last the quest is
    /// ready to turn in. Re-entry for the same quest (a stage-start hook that advances one of its objectives) returns
    /// at once and this loop sees the change on its next pass; any other quest settles in full.
    /// </summary>
    private void Settle(CharacterEntity character, QuestView quest, ActiveQuest active)
    {
        if (!_settling.Add(active))
            return;

        try
        {
            while (active.State == CharacterQuestState.Active
                   && character.Quests.Get(quest.Id) == active
                   && CurrentStage(quest, active) is { } stage
                   && stage.Objectives.All(o => active.ProgressOf(o.Id) >= o.Count))
            {
                if (active.Stage + 1 < quest.Stages.Count)
                {
                    character.Quests.SetStage(active, active.Stage + 1);
                    character.Quests.Say($"{Title(character, quest)}: stage complete.");
                    StageStarted(character, quest, active);
                }
                else
                {
                    character.Quests.SetState(active, CharacterQuestState.ReadyToTurnIn);
                    character.Quests.Say($"{Title(character, quest)}: ready to turn in.");
                }
            }
        }
        finally
        {
            _settling.Remove(active);
        }
    }

    /// <summary>
    /// An open conversation with <paramref name="npcGuid" /> the character may act on: alive, the NPC in its
    /// instance and alive, inside the dialogue leash. A gone or dead NPC, or one past the leash, ends the
    /// conversation out loud, as DialogueChooseHandler does.
    /// </summary>
    private QuestResult Conversation(IWorldConnection connection, CharacterEntity character, ulong npcGuid, out ICreature? npc)
    {
        npc = null;
        if (character.IsDead || connection.CurrentDialogue is not { } open || open.Npc.RawValue != npcGuid)
            return QuestResult.NoConversation;

        if (world.InstanceRegistry.GetInstanceById(character.InstanceId) is not { } instance
            || !instance.Creatures.TryGetValue(open.Npc, out ICreature? found)
            || found.CurrentHealth == 0)
        {
            NpcInteraction.EndConversation(connection, open.Npc);
            return QuestResult.NoConversation;
        }

        if (!NpcInteraction.IsWithinLeash(character.Position, found.Position))
        {
            NpcInteraction.EndConversation(connection, open.Npc);
            return QuestResult.TooFar;
        }

        npc = found;
        return QuestResult.Ok;
    }

    private string Title(CharacterEntity character, QuestView quest) => Text(character, quest.TitleTextId);

    private string Text(CharacterEntity character, LocalizedTextId id)
    {
        ILocalizedTextCatalog text = world.Data.LocalizedTexts;
        return text.Get(id, text.ContextFor(character, character.Quests.Locale));
    }

    // Seams the later tasks fill. Each is a no-op here so this task stands on its own.

    /// <summary>Task 9: the quest script's CanAccept.</summary>
    private bool ScriptAllows(QuestView quest, CharacterEntity character) => true;

    /// <summary>Task 9: the quest script's OnAccepted.</summary>
    private void OnAccepted(CharacterEntity character, QuestView quest, ActiveQuest active)
    {
    }

    /// <summary>Task 7: recount the new stage's Collect objectives. Task 9: the script's OnStageStarted.</summary>
    private void StageStarted(CharacterEntity character, QuestView quest, ActiveQuest active)
    {
        AfterStageStarted?.Invoke(character, quest, active);
    }

    /// <summary>Task 7: every copy of the quest's Collect items leaves the Bag and the Bank.</summary>
    private void RemoveQuestItems(CharacterEntity character, QuestView quest)
    {
    }

    /// <summary>Task 5: re-send the NPC's root so its quest options are current.</summary>
    private void AfterConversationChange(IWorldConnection connection, CharacterEntity character, ICreature npc)
    {
    }
}
