using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.World;
using Avalon.World.Dialogue;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Enums;
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
    /// The quest options this character gets at this node (#433): none unless it is the NPC's root; otherwise one
    /// QuestTurnIn per quest ready to hand in here (by id), then one QuestOffer per quest this NPC gives that the
    /// character may accept, by level then id. A quest refused only because the log is full is still offered, so
    /// the accept can say LogFull. Each option's id is the quest id negated.
    /// </summary>
    public List<SDialogueOptionInfo> DialogueOptionsFor(IWorldConnection connection, CharacterEntity character, ICreature npc, DialogueNodeView node)
    {
        if (NpcInteraction.RootFor(world.Data.Dialogue, npc.Metadata.Id) is not { } root || root.Id.Value != node.Id.Value)
            return [];

        QuestCatalog catalog = Catalog;
        ILocalizedTextCatalog text = world.Data.LocalizedTexts;
        TextContext context = text.ContextFor(character, connection.Locale);
        List<SDialogueOptionInfo> options = [];

        foreach (QuestView quest in catalog.EndedBy(npc.Metadata.Id))
        {
            if (character.Quests.Get(quest.Id) is { State: CharacterQuestState.ReadyToTurnIn })
                options.Add(Option(quest, DialogueOptionKind.QuestTurnIn, text, context));
        }

        foreach (QuestView quest in catalog.GivenBy(npc.Metadata.Id))
        {
            if (Availability(character, quest) is QuestResult.Ok or QuestResult.LogFull)
                options.Add(Option(quest, DialogueOptionKind.QuestOffer, text, context));
        }

        return options;
    }

    /// <summary>
    /// A chosen dialogue option whose id is negative is a quest option (#433). When this character is offered it at
    /// this node now, the quest's offer goes out; otherwise it is logged and ignored. The conversation stays where
    /// it is either way. False only for an authored (non-negative) id, which the caller handles.
    /// </summary>
    public bool TryChoose(IWorldConnection connection, CharacterEntity character, ICreature npc, DialogueNodeView node, int optionId)
    {
        if (optionId >= 0)
            return false;

        SDialogueOptionInfo? offered = DialogueOptionsFor(connection, character, npc, node).FirstOrDefault(o => o.OptionId == optionId);
        if (offered?.QuestId is not { } questId || !Catalog.TryGet(questId, out QuestView? quest))
        {
            logger.LogInformation("Quest option {Option} is not offered to character {CharacterId} by {Npc}",
                optionId, character.Guid.Id, npc.Metadata.Id.Value);
            return true;
        }

        bool turnIn = offered.Kind == DialogueOptionKind.QuestTurnIn;
        ILocalizedTextCatalog text = world.Data.LocalizedTexts;
        connection.Send(SQuestOfferPacket.Create(questId, npc.Guid.RawValue,
            turnIn ? QuestOfferMode.TurnIn : QuestOfferMode.Offer,
            QuestDisplay.Build(quest, text, text.ContextFor(character, connection.Locale), completionText: turnIn),
            connection.CryptoSession.Encrypt));
        return true;
    }

    private static SDialogueOptionInfo Option(QuestView quest, DialogueOptionKind kind, ILocalizedTextCatalog text, TextContext context) => new()
    {
        OptionId = -(int)quest.Id,
        Text = text.Get(quest.TitleTextId, context),
        Kind = kind,
        QuestId = quest.Id,
    };

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
    /// 0, a quest that is not active (or is ready), unknown to the catalog, an objective outside the current stage,
    /// or a Collect objective, whose count comes only from the Bag (RecountCollect).
    /// </summary>
    public bool AddProgress(CharacterEntity character, uint questId, uint objectiveId, uint amount)
    {
        if (amount == 0
            || character.Quests.Get(questId) is not { State: CharacterQuestState.Active } active
            || !Catalog.TryGet(questId, out QuestView? quest)
            || CurrentStage(quest, active)?.Objectives.FirstOrDefault(o => o.Id == objectiveId) is not { } objective
            || objective.Type == Domain.World.QuestObjectiveType.Collect)
            return false;

        ulong total = (ulong)active.ProgressOf(objectiveId) + amount;
        return SetCount(character, quest, active, objective, (uint)Math.Min(total, objective.Count));
    }

    /// <summary>
    /// A creature died (#433): every character that shares the kill (the PartyEligibility list MapInstance took
    /// once, at the kill) gets +1, capped, on each Kill objective for this creature in the current stage of each of
    /// its Active quests. Dead members count, as they do for loot and experience; the list already leaves out a
    /// killer or member in a leave countdown.
    /// </summary>
    public void CreatureKilled(ICreature creature, IReadOnlyList<ICharacter> eligible)
    {
        ulong template = creature.Metadata.Id.Value;
        QuestCatalog catalog = Catalog;
        foreach (ICharacter member in eligible)
        {
            if (member is not CharacterEntity character)
                continue;

            // A copy: settling a quest never removes one, but a script hook (Task 9) must not see a moving collection.
            foreach (ActiveQuest active in character.Quests.Active.ToList())
            {
                if (active.State != CharacterQuestState.Active || !catalog.TryGet(active.QuestId, out QuestView? quest))
                    continue;

                if (CurrentStage(quest, active) is { } stage)
                {
                    foreach (QuestObjectiveView objective in stage.Objectives)
                    {
                        if (objective.Type == Domain.World.QuestObjectiveType.Kill && objective.CreatureTemplateId?.Value == template)
                            AddProgress(character, quest.Id, objective.Id, 1);
                    }
                }

                OnKilled(character, quest, active, creature);
            }
        }
    }

    /// <summary>
    /// The character opened a conversation with <paramref name="npc" /> (#433): every Talk objective for its
    /// template in the current stage of an Active quest is met. InteractHandler calls this only for an interact it
    /// accepted (alive, in range, an NPC with dialogue).
    /// </summary>
    public void Interacted(CharacterEntity character, ICreature npc)
    {
        ulong template = npc.Metadata.Id.Value;
        QuestCatalog catalog = Catalog;
        foreach (ActiveQuest active in character.Quests.Active.ToList())
        {
            if (active.State != CharacterQuestState.Active || !catalog.TryGet(active.QuestId, out QuestView? quest))
                continue;

            if (CurrentStage(quest, active) is { } stage)
            {
                foreach (QuestObjectiveView objective in stage.Objectives)
                {
                    if (objective.Type == Domain.World.QuestObjectiveType.Talk && objective.CreatureTemplateId?.Value == template)
                        AddProgress(character, quest.Id, objective.Id, objective.Count);
                }
            }

            OnInteracted(character, quest, active, npc);
        }
    }

    /// <summary>
    /// Quest item drops for a kill (#433): for each eligible character, each drop row of this creature whose
    /// objective is an unmet Collect objective in the current stage of one of its Active quests is rolled once
    /// (ILootRandom, a percentage); each success is one item, reserved to that character for good.
    /// </summary>
    public IReadOnlyList<(RolledDrop Drop, uint Owner)> RollQuestDrops(ICreature creature, IReadOnlyList<ICharacter> eligible)
    {
        QuestCatalog catalog = Catalog;
        IReadOnlyList<QuestDropView> drops = catalog.DropsFrom(creature.Metadata.Id);
        if (drops.Count == 0 || eligible.Count == 0)
            return [];

        List<(RolledDrop, uint)> rolled = [];
        foreach (ICharacter member in eligible)
        {
            if (member is not CharacterEntity character)
                continue;

            foreach (QuestDropView drop in drops)
            {
                if (character.Quests.Get(drop.QuestId) is not { } active
                    || !catalog.TryGet(drop.QuestId, out QuestView? quest)
                    || CurrentStage(quest, active)?.Objectives.FirstOrDefault(o => o.Id == drop.ObjectiveId) is not { } objective
                    || !StillNeeds(character, active, objective))
                    continue;

                if (random.NextDouble() * 100.0 < drop.Chance)
                    rolled.Add((RolledDrop.Item(drop.ItemTemplateId, 1), character.Guid.Id));
            }
        }

        return rolled;
    }

    /// <summary>
    /// Sets every Collect objective in the current stage of each held quest to what the Bag holds, capped (#433).
    /// A quest that was ready and is now short goes back to Active; one that now has everything settles.
    /// QuestFlusher calls this on a tick whose inventory changes touched the Bag.
    /// </summary>
    public void RecountCollect(CharacterEntity character)
    {
        QuestCatalog catalog = Catalog;
        foreach (ActiveQuest active in character.Quests.Active.ToList())
        {
            if (catalog.TryGet(active.QuestId, out QuestView? quest))
                Recount(character, quest, active);
        }
    }

    /// <summary>
    /// Whether the character may take a drop of this item (#433). Anything but a quest item, or an item whose
    /// template is gone (the pickup refuses that itself), may be taken. A quest item only while a Collect objective
    /// for it in the current stage of one of its Active quests still lacks some in the Bag: a drop rolled for a
    /// quest since abandoned, handed in or moved past that stage stays on the ground.
    /// </summary>
    public bool MayPickUp(CharacterEntity character, ItemTemplateId item)
    {
        if (world.Data.ItemTemplates.FirstOrDefault(t => t.Id == item) is not { } template
            || !template.Flags.HasFlag(Domain.World.ItemTemplateFlags.QuestItem))
            return true;

        QuestCatalog catalog = Catalog;
        foreach (ActiveQuest active in character.Quests.Active)
        {
            if (catalog.TryGet(active.QuestId, out QuestView? quest)
                && CurrentStage(quest, active) is { } stage
                && stage.Objectives.Any(o => o.ItemTemplateId?.Value == item.Value && StillNeeds(character, active, o)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The one "still needs this quest item" rule (#433), for the drop roll and the pickup alike: an Active quest's
    /// Collect objective (the caller passes one of its current stage) while the Bag holds fewer than its count. The
    /// Bag, never the recorded progress, which is recounted only at the end of the tick: a pickup and a kill in the
    /// same tick must agree, or a drop could be reserved for a character that may never take it.
    /// </summary>
    private static bool StillNeeds(CharacterEntity character, ActiveQuest active, QuestObjectiveView objective) =>
        active.State == CharacterQuestState.Active
        && objective.Type == Domain.World.QuestObjectiveType.Collect
        && objective.ItemTemplateId is { } item
        && HeldInBag(character, item) < objective.Count;

    public static long HeldInBag(CharacterEntity character, ItemTemplateId item) =>
        character.Container(InventoryType.Bag).Items.Where(i => i.TemplateId.Value == item.Value).Sum(i => (long)i.Count);

    private void Recount(CharacterEntity character, QuestView quest, ActiveQuest active)
    {
        if (CurrentStage(quest, active) is not { } stage)
            return;

        bool changed = false;
        foreach (QuestObjectiveView objective in stage.Objectives)
        {
            if (objective.Type != Domain.World.QuestObjectiveType.Collect)
                continue;

            uint held = (uint)Math.Min(HeldInBag(character, objective.ItemTemplateId!), objective.Count);
            if (!character.Quests.SetProgress(active, objective.Id, held))
                continue;

            changed = true;
            character.Quests.Say($"{Text(character, objective.DescriptionTextId)}: {held}/{objective.Count}");
            if (held < objective.Count && active.State == CharacterQuestState.ReadyToTurnIn)
            {
                character.Quests.SetState(active, CharacterQuestState.Active);
                character.Quests.Say($"{Title(character, quest)}: no longer ready to turn in.");
            }
        }

        if (changed)
            Settle(character, quest, active);
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

    /// <summary>
    /// A stage started: its Collect objectives count what the Bag already holds (#433). Task 9: the script's
    /// OnStageStarted.
    /// </summary>
    private void StageStarted(CharacterEntity character, QuestView quest, ActiveQuest active)
    {
        Recount(character, quest, active);
        AfterStageStarted?.Invoke(character, quest, active);
    }

    /// <summary>Task 9: the quest script's OnCreatureKilled.</summary>
    private void OnKilled(CharacterEntity character, QuestView quest, ActiveQuest active, ICreature creature)
    {
    }

    /// <summary>Task 9: the quest script's OnInteract.</summary>
    private void OnInteracted(CharacterEntity character, QuestView quest, ActiveQuest active, ICreature npc)
    {
    }

    /// <summary>Every copy of the quest's Collect items leaves the Bag and the Bank (turn-in and abandon, #433).</summary>
    private void RemoveQuestItems(CharacterEntity character, QuestView quest)
    {
        IInventoryService inventory = economy.InventoryOf(character);
        foreach (ulong item in quest.Objectives.Where(o => o.Type == Domain.World.QuestObjectiveType.Collect)
                     .Select(o => o.ItemTemplateId!.Value).Distinct())
        {
            foreach (InventoryType container in (InventoryType[])[InventoryType.Bag, InventoryType.Bank])
            {
                foreach (InventoryItem stack in character.Container(container).Items.Where(i => i.TemplateId.Value == item).ToList())
                {
                    InventoryRemoveResult removed = inventory.TryRemove(stack.InstanceId, stack.Count);
                    if (removed != InventoryRemoveResult.Ok)
                        logger.LogWarning("Could not take quest item {Item} ({Result}) from character {CharacterId}",
                            item, removed, character.Guid.Id);
                }
            }
        }
    }

    /// <summary>After an accept or a turn-in, the NPC's root again, so its quest options are current.</summary>
    private void AfterConversationChange(IWorldConnection connection, CharacterEntity character, ICreature npc)
    {
        if (NpcInteraction.RootFor(world.Data.Dialogue, npc.Metadata.Id) is not { } root)
            return;

        connection.CurrentDialogue = (npc.Guid, root.Id);
        InteractHandler.Send(connection, npc, root, character, world.Data, DialogueOptionsFor(connection, character, npc, root));
    }
}
