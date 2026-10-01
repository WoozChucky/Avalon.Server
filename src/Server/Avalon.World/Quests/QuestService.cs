using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.World;
using Avalon.World.Characters;
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
using Avalon.World.Public.Scripts;
using Microsoft.Extensions.DependencyInjection;
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

    // One script per quest, built on first use and shared by every character (#433), with the type it was built for. A
    // null script could not be built: logged once, and its quest cannot be accepted. Tick thread only.
    private readonly Dictionary<uint, (Type Type, QuestScript? Script)> _scripts = [];

    // When each quest's script last had a throw (or a refused Advance) logged, and how many were left out since; 10 s
    // apart, per quest.
    private readonly Dictionary<uint, (DateTimeOffset LastLogged, int Suppressed)> _scriptErrors = [];
    private readonly Dictionary<uint, (DateTimeOffset LastLogged, int Suppressed)> _refusedAdvances = [];

    // When building each quest's log entry or update last threw and was logged, and how many were left out since.
    private readonly Dictionary<uint, (DateTimeOffset LastLogged, int Suppressed)> _clientBuildErrors = [];

    // When re-sending an NPC's root after an accept or turn-in last threw and was logged, per quest (final review M1).
    private readonly Dictionary<uint, (DateTimeOffset LastLogged, int Suppressed)> _rootResendErrors = [];

    // One throttled log per QuestFlusher step (final review M2). Tick thread only.
    private readonly Dictionary<string, ThrottledErrorLog> _flushStepErrors = new(StringComparer.Ordinal);

    // What quest scripts are built from (#738): a logger factory, loggers and the clock, never a service that writes.
    private readonly QuestScriptServices _scriptServices = new(services);

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

        // Stage 0 starts before anything settles: progress the accept hook or the bag gives it waits for the Settle
        // below, so each stage's hook is heard exactly once, in order, stage 0 first.
        _settling.Add(active);
        try
        {
            OnAccepted(character, quest, active);
            StageStarted(character, quest, active);
        }
        finally
        {
            _settling.Remove(active);
        }

        Settle(character, quest, active);
        AfterConversationChange(connection, character, npc, questId);
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
    /// CMSG_QUEST_TURN_IN (#433), all or nothing: an open conversation with the quest's ender inside the leash, the
    /// quest held, the bag recounted as it is now, then QuestTurnInRules. Only then do the quest items leave, and
    /// experience (personal: no party split, no level gap, no map band), money and items are paid.
    /// </summary>
    public QuestResult TurnIn(IWorldConnection connection, CharacterEntity character, uint questId, ulong npcGuid)
    {
        QuestResult talk = Conversation(connection, character, npcGuid, out ICreature? npc);
        if (talk != QuestResult.Ok)
            return talk;

        if (character.Quests.Get(questId) is not { } active || !Catalog.TryGet(questId, out QuestView? quest))
            return QuestResult.NotActive;

        if (quest.EnderCreatureId.Value != npc!.Metadata.Id.Value)
            return QuestResult.NoConversation;

        // The bag as it is now: an item destroyed this tick has not been through QuestFlusher yet.
        Recount(character, quest, active);

        QuestResult decision = QuestTurnInRules.Decide(character, quest, active, FindTemplate, world.Configuration.MaxMoney,
            out IReadOnlyList<(ItemTemplate Template, uint Count)> rewards);
        if (decision == QuestResult.Error)
            LogUnpayableRewards(quest);
        if (decision != QuestResult.Ok)
            return decision;

        RemoveQuestItems(character, quest);
        ExperienceAward.Grant(character, quest.RewardExperience, world.Data, parties, logger);

        if (quest.RewardMoney > 0 && economy.WalletOf(character).TryAddMoney(quest.RewardMoney) != WalletResult.Ok)
            logger.LogError("Quest {QuestId} money was refused after its checks passed for character {CharacterId}", questId, character.Guid.Id);

        IInventoryService inventory = economy.InventoryOf(character);
        foreach ((ItemTemplate template, uint count) in rewards)
        {
            InventoryAddResult added = inventory.TryAdd(template.Id, count);
            if (added != InventoryAddResult.Ok)
                logger.LogError("Quest {QuestId} reward {Item} was refused ({Result}) after its checks passed for character {CharacterId}",
                    questId, template.Id.Value, added, character.Guid.Id);
        }

        character.Quests.Complete(questId, time.GetUtcNow().UtcDateTime);
        character.Quests.Say($"Quest completed: {Title(character, quest)}.");
        AfterConversationChange(connection, character, npc, questId);
        return QuestResult.Ok;
    }

    /// <summary>
    /// A reward template that is gone or Unique blocks every turn-in of the quest (a reload since the catalog was
    /// built), so it is logged at Error, naming the quest and the template.
    /// </summary>
    private void LogUnpayableRewards(QuestView quest)
    {
        foreach (QuestItemRewardView reward in quest.ItemRewards)
        {
            ItemTemplate? template = FindTemplate(reward.ItemTemplateId);
            if (template is null || template.Flags.HasFlag(ItemTemplateFlags.Unique))
                logger.LogError("Quest {QuestId} cannot be turned in: reward item template {Item} is {Problem}",
                    quest.Id, reward.ItemTemplateId.Value, template is null ? "missing" : "Unique");
        }
    }

    private ItemTemplate? FindTemplate(ItemTemplateId id) =>
        world.Data.ItemTemplates.FirstOrDefault(t => t.Id.Value == id.Value);

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

            // A copy: settling a quest never removes one, but a script hook must not see a moving collection.
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
    /// The select-time recount (#433): every held quest settled against the current catalog (SettleHeld, which
    /// recounts the Collect objectives), then what it owes the client is dropped (the save marks stay). The whole log
    /// the client gets on entering the world already shows every count and state the recount set, so its milestone
    /// lines and updates would only repeat it as fresh progress. It records the catalog it settled against, so the
    /// flush settles again only once a /reload quests lands (#738).
    /// </summary>
    public void RecountAtSelect(CharacterEntity character)
    {
        try
        {
            QuestCatalog catalog = Catalog;
            character.Quests.SettledCatalog = catalog;
            SettleHeld(character, catalog);
        }
        finally
        {
            character.Quests.ClearClientChanges();
        }
    }

    /// <summary>
    /// After a /reload quests (#738), once, on the first flush that sees the new catalog: every held quest is settled
    /// against it (SettleHeld), so a reload that lowered a count or removed an objective completes the stage, or makes
    /// the quest ready, at once, with the usual lines and updates. A session that has not settled yet (no select
    /// recount) only records the catalog. The catalog is recorded first, so a settle that throws is not retried every
    /// tick.
    /// </summary>
    public void SettleAfterReload(CharacterEntity character)
    {
        QuestCatalog catalog = Catalog;
        QuestLog log = character.Quests;
        if (ReferenceEquals(log.SettledCatalog, catalog))
            return;

        bool settledBefore = log.SettledCatalog is not null;
        log.SettledCatalog = catalog;
        if (settledBefore)
            SettleHeld(character, catalog);
    }

    /// <summary>
    /// Every held quest the catalog has, against it: each count above its objective's count is capped to it (a reload
    /// lowered it), the current stage's Collect objectives are recounted from the Bag, and the stages settle. A quest
    /// the catalog lacks is left as it is.
    /// </summary>
    private void SettleHeld(CharacterEntity character, QuestCatalog catalog)
    {
        // A copy: a stage-start hook must not see a moving collection.
        foreach (ActiveQuest active in character.Quests.Active.ToList())
        {
            if (!catalog.TryGet(active.QuestId, out QuestView? quest))
                continue;

            foreach (QuestObjectiveView objective in quest.Objectives)
            {
                if (active.ProgressOf(objective.Id) > objective.Count)
                    character.Quests.SetProgress(active, objective.Id, objective.Count);
            }

            Recount(character, quest, active);
            Settle(character, quest, active);
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

    // The quest script's hooks (#433). Each runs contained (RunHook): a throw is logged and the quest, the kill, the
    // interact, the accept or the select that called it goes on, as does every other quest's hook.

    /// <summary>The quest script's CanAccept (#433): no script allows; a script that cannot be built, or whose CanAccept throws, refuses.</summary>
    private bool ScriptAllows(QuestView quest, CharacterEntity character)
    {
        if (quest.ScriptType is null)
            return true;
        if (ScriptOf(quest) is not { } script)
            return false;

        bool allows = false;
        return Contained(quest, character, nameof(QuestScript.CanAccept),
            () => allows = script.CanAccept(new QuestCharacterView(character))) && allows;
    }

    private void OnAccepted(CharacterEntity character, QuestView quest, ActiveQuest active) =>
        RunHook(character, quest, active, nameof(QuestScript.OnAccepted), static (script, context) => script.OnAccepted(context));

    /// <summary>
    /// A stage started (stage 0 at accept): its Collect objectives count what the Bag already holds, then the script
    /// hears of it. Needs no instance, so it also runs for a character still being selected (the select recount).
    /// </summary>
    private void StageStarted(CharacterEntity character, QuestView quest, ActiveQuest active)
    {
        int stage = active.Stage;
        Recount(character, quest, active);
        RunHook(character, quest, active, nameof(QuestScript.OnStageStarted), (script, context) => script.OnStageStarted(context, stage));
    }

    /// <summary>The kill hook, only while the quest is still Active: a kill that made it ready reaches no hook.</summary>
    private void OnKilled(CharacterEntity character, QuestView quest, ActiveQuest active, ICreature creature)
    {
        if (StillActive(character, quest, active))
            RunHook(character, quest, active, nameof(QuestScript.OnCreatureKilled),
                (script, context) => script.OnCreatureKilled(context, QuestCreatureView.From(creature)));
    }

    /// <summary>The interact hook, only while the quest is still Active: a talk that made it ready reaches no hook.</summary>
    private void OnInteracted(CharacterEntity character, QuestView quest, ActiveQuest active, ICreature npc)
    {
        if (StillActive(character, quest, active))
            RunHook(character, quest, active, nameof(QuestScript.OnInteract),
                (script, context) => script.OnInteract(context, QuestCreatureView.From(npc)));
    }

    private static bool StillActive(CharacterEntity character, QuestView quest, ActiveQuest active) =>
        active.State == CharacterQuestState.Active && character.Quests.Get(quest.Id) == active;

    /// <summary>
    /// OnEnterInstance for every held Active quest with a script (#433), on each arrival in an instance other than the
    /// one it last ran for: QuestFlusher calls this every tick. The memo is never saved, so every login is an arrival.
    /// A character with no live instance (still being selected, or between instances) is skipped and tried again on
    /// a later tick, so the hook always gets a real instance. The view is built inside each hook's containment, and
    /// the memo is written once the hooks ran.
    /// </summary>
    public void EnteredInstanceIfChanged(CharacterEntity character)
    {
        if (character.Quests.ScriptsInstance == character.InstanceId
            || world.InstanceRegistry.GetInstanceById(character.InstanceId) is not { } instance)
            return;

        QuestCatalog catalog = Catalog;
        QuestInstanceView? view = null;
        foreach (ActiveQuest active in character.Quests.Active.ToList())
        {
            if (!catalog.TryGet(active.QuestId, out QuestView? quest) || quest.ScriptType is null || !StillActive(character, quest, active))
                continue;

            RunHook(character, quest, active, nameof(QuestScript.OnEnterInstance),
                (script, context) => script.OnEnterInstance(context, view ??= QuestInstanceView.From(instance)));
        }

        character.Quests.ScriptsInstance = character.InstanceId;
    }

    /// <summary>
    /// IQuestContext.Advance (#433): +<paramref name="amount" />, capped, on one of this quest's Scripted objectives in
    /// its current stage, as the live catalog has it (a context kept across a /reload is judged by the reloaded
    /// quest), while the character still holds this very copy of the quest and it is Active. Anything else is
    /// refused and logged at Warning, at most once per quest per 10 s.
    /// </summary>
    internal bool ScriptAdvance(CharacterEntity character, QuestView quest, ActiveQuest active, uint objectiveId, uint amount)
    {
        bool allowed = amount > 0
                       && StillActive(character, quest, active)
                       && Catalog.TryGet(quest.Id, out QuestView? live)
                       && CurrentStage(live, active)?.Objectives.FirstOrDefault(o => o.Id == objectiveId) is
                           { Type: Domain.World.QuestObjectiveType.Scripted };
        if (allowed)
            return AddProgress(character, quest.Id, objectiveId, amount);

        if (!Throttled(_refusedAdvances, quest.Id, out int suppressed))
            logger.LogWarning("Quest {QuestId}'s script tried to advance objective {Objective} by {Amount} for character {CharacterId}; " +
                              "only its own Scripted objectives in the current stage can be. {Suppressed} earlier refusals were not logged",
                quest.Id, objectiveId, amount, character.Guid.Id, suppressed);
        return false;
    }

    /// <summary>
    /// True when this quest logged within <see cref="ThrottledErrorLog.Interval" /> (and it is counted as left out);
    /// otherwise false, with the count left out since the last log, and the clock restarted.
    /// </summary>
    private bool Throttled(Dictionary<uint, (DateTimeOffset LastLogged, int Suppressed)> logged, uint questId, out int suppressed)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (logged.TryGetValue(questId, out (DateTimeOffset LastLogged, int Suppressed) last) && now - last.LastLogged < ThrottledErrorLog.Interval)
        {
            logged[questId] = (last.LastLogged, last.Suppressed + 1);
            suppressed = 0;
            return true;
        }

        suppressed = last.Suppressed;
        logged[questId] = (now, 0);
        return false;
    }

    /// <summary>The quest's script, built on first use; null for a quest with none or one that could not be built.</summary>
    private QuestScript? ScriptOf(QuestView quest)
    {
        if (quest.ScriptType is not { } type)
            return null;

        // A reload can point the quest at another script; the cached one is kept only while it is still that type.
        if (_scripts.TryGetValue(quest.Id, out (Type Type, QuestScript? Script) cached) && cached.Type == type)
            return cached.Script;

        QuestScript? built = null;
        try
        {
            built = (QuestScript)ActivatorUtilities.CreateInstance(_scriptServices, type);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Quest {QuestId} script {Script} could not be built; the quest cannot be accepted", quest.Id, type.Name);
        }

        _scripts[quest.Id] = (type, built);
        return built;
    }

    private void RunHook(CharacterEntity character, QuestView quest, ActiveQuest active, string hook, Action<QuestScript, IQuestContext> call)
    {
        if (ScriptOf(quest) is { } script)
            Contained(quest, character, hook, () => call(script, new QuestContext(this, character, quest, active)));
    }

    /// <summary>
    /// Runs one hook of a quest's script, contained: a throw is logged at Error with the quest, the hook and the
    /// character, at most once per quest per <see cref="ThrottledErrorLog.Interval" /> (counting the throws left out),
    /// and false is returned; the caller goes on.
    /// </summary>
    private bool Contained(QuestView quest, CharacterEntity character, string hook, Action call)
    {
        try
        {
            call();
            return true;
        }
        catch (Exception e)
        {
            if (!Throttled(_scriptErrors, quest.Id, out int suppressed))
                logger.LogError(e, "Quest {QuestId} script {Script} threw in {Hook} for character {CharacterId}; the quest went on. " +
                                   "{Suppressed} earlier throws of this quest's script were not logged",
                    quest.Id, quest.ScriptType?.Name, hook, character.Guid.Id, suppressed);
            return false;
        }
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

    /// <summary>
    /// What the client is owed this tick (#433): the whole log the first time (which carries every change already, so
    /// the pending updates are dropped), otherwise one SMSG_QUEST_UPDATE per changed quest in id order; then the
    /// milestone lines on the system channel. Clears what it sent.
    /// </summary>
    public void FlushClient(IWorldConnection connection, CharacterEntity character)
    {
        QuestLog log = character.Quests;
        if (!log.LogSent)
        {
            connection.Send(LogPacket(connection, character));
            log.LogSent = true;
        }
        else if (log.ClientChanges.Count > 0)
        {
            foreach ((uint questId, QuestClientChange change) in log.ClientChanges.OrderBy(c => c.Key))
            {
                NetworkPacket? update = null;
                try
                {
                    update = UpdatePacket(connection, character, questId, change);
                }
                catch (Exception e)
                {
                    LogClientBuildFailure(e, character, questId, "update");
                }

                if (update is not null)
                    connection.Send(update);
            }
        }

        if (log.PendingLines.Count > 0)
        {
            DateTime now = time.GetUtcNow().UtcDateTime;
            foreach (string line in log.PendingLines)
                connection.Send(SChatMessagePacket.System(line, now, connection.CryptoSession.Encrypt));
        }

        log.ClearClientChanges();
    }

    private NetworkPacket LogPacket(IWorldConnection connection, CharacterEntity character)
    {
        QuestCatalog catalog = Catalog;
        ILocalizedTextCatalog text = world.Data.LocalizedTexts;
        TextContext context = text.ContextFor(character, connection.Locale);
        List<QuestLogEntryDto> entries = [];
        foreach (ActiveQuest active in character.Quests.Active.OrderBy(a => a.QuestId))
        {
            if (!catalog.TryGet(active.QuestId, out QuestView? quest))
            {
                logger.LogWarning("Character {CharacterId} holds quest {QuestId}, which the catalog does not have; it is left out of the log",
                    character.Guid.Id, active.QuestId);
                continue;
            }

            // One quest whose entry cannot be built is left out; the log still goes out with the others.
            try
            {
                entries.Add(new QuestLogEntryDto
                {
                    QuestId = quest.Id,
                    State = StateOf(active.State),
                    Stage = active.Stage,
                    Display = QuestDisplay.Build(quest, text, context, completionText: false),
                    Progress = ProgressOf(quest, active),
                });
            }
            catch (Exception e)
            {
                LogClientBuildFailure(e, character, quest.Id, "log entry");
            }
        }

        return SQuestLogPacket.Create(entries, character.Quests.Completed.Order().ToList(), connection.CryptoSession.Encrypt);
    }

    private NetworkPacket? UpdatePacket(IWorldConnection connection, CharacterEntity character, uint questId, QuestClientChange change)
    {
        var update = new SQuestUpdatePacket { QuestId = questId };
        switch (change)
        {
            case QuestClientChange.Removed:
                update.Kind = QuestUpdateKind.Removed;
                break;
            case QuestClientChange.Completed:
                update.Kind = QuestUpdateKind.Completed;
                break;
            default:
                if (character.Quests.Get(questId) is not { } active || !Catalog.TryGet(questId, out QuestView? quest))
                    return null;

                update.Kind = change == QuestClientChange.Accepted ? QuestUpdateKind.Accepted : QuestUpdateKind.Progress;
                update.State = StateOf(active.State);
                update.Stage = active.Stage;
                update.Progress = ProgressOf(quest, active);
                if (change == QuestClientChange.Accepted)
                {
                    ILocalizedTextCatalog text = world.Data.LocalizedTexts;
                    update.Display = QuestDisplay.Build(quest, text, text.ContextFor(character, connection.Locale), completionText: false);
                }

                break;
        }

        return SQuestUpdatePacket.Create(update, connection.CryptoSession.Encrypt);
    }

    /// <summary>Logged at Error at most once per quest per <see cref="ThrottledErrorLog.Interval" />, counting the throws left out.</summary>
    private void LogClientBuildFailure(Exception e, CharacterEntity character, uint questId, string what)
    {
        if (!Throttled(_clientBuildErrors, questId, out int suppressed))
            logger.LogError(e, "Building the quest {What} for quest {QuestId} and character {CharacterId} failed; it was left out. " +
                               "{Suppressed} earlier failures for this quest were not logged",
                what, questId, character.Guid.Id, suppressed);
    }

    private static QuestStateKind StateOf(CharacterQuestState state) => state switch
    {
        CharacterQuestState.Active => QuestStateKind.Active,
        CharacterQuestState.ReadyToTurnIn => QuestStateKind.ReadyToTurnIn,
        _ => QuestStateKind.Unknown,
    };

    private static List<QuestProgressDto> ProgressOf(QuestView quest, ActiveQuest active) =>
        quest.Objectives.Select(o => new QuestProgressDto { ObjectiveId = o.Id, Progress = active.ProgressOf(o.Id) }).ToList();

    /// <summary>
    /// SMSG_QUEST_MARKERS (#433): every quest NPC in the character's instance with its marker for this character,
    /// worked out again only when the instance, the level, the quest log or the catalog generation changed since the
    /// last time, and sent only when the list differs from the one last sent.
    /// </summary>
    public void FlushMarkers(IWorldConnection connection, CharacterEntity character)
    {
        QuestLog log = character.Quests;
        QuestCatalog catalog = Catalog;
        if (log.MarkersInstance == character.InstanceId && log.MarkersLevel == character.Level
            && log.MarkersVersion == log.Version && ReferenceEquals(log.MarkersCatalog, catalog))
            return;

        if (world.InstanceRegistry.GetInstanceById(character.InstanceId) is not { } instance)
            return;

        log.MarkersInstance = character.InstanceId;
        log.MarkersLevel = character.Level;
        log.MarkersVersion = log.Version;
        log.MarkersCatalog = catalog;

        List<(ulong Creature, byte Marker)> markers = [];
        foreach (ICreature creature in instance.Creatures.Values)
        {
            if (catalog.IsQuestNpc(creature.Metadata.Id))
                markers.Add((creature.Guid.RawValue, (byte)MarkerFor(character, creature.Metadata.Id)));
        }

        markers.Sort();
        if (log.MarkersSent is { } sent && sent.SequenceEqual(markers))
            return;

        log.MarkersSent = markers;
        connection.Send(SQuestMarkersPacket.Create(
            markers.Select(m => new QuestMarkerDto { CreatureGuid = m.Creature, Marker = (QuestMarker)m.Marker }).ToList(),
            connection.CryptoSession.Encrypt));
    }

    /// <summary>A ready turn-in outranks an available quest; a quest refused only by a full log still shows as available.</summary>
    public QuestMarker MarkerFor(CharacterEntity character, CreatureTemplateId npc)
    {
        QuestCatalog catalog = Catalog;
        foreach (QuestView quest in catalog.EndedBy(npc))
        {
            if (character.Quests.Get(quest.Id) is { State: CharacterQuestState.ReadyToTurnIn })
                return QuestMarker.ReadyToTurnIn;
        }

        foreach (QuestView quest in catalog.GivenBy(npc))
        {
            if (Availability(character, quest) is QuestResult.Ok or QuestResult.LogFull)
                return QuestMarker.Available;
        }

        return QuestMarker.None;
    }

    /// <summary>
    /// After an accept or a turn-in, the NPC's root again, so its quest options are current. Contained (final review
    /// M1): the accept or turn-in has already happened, so a throw here is logged at Error, throttled per quest, and
    /// the request is still answered Ok; the client keeps the node it had until it talks to the NPC again.
    /// </summary>
    private void AfterConversationChange(IWorldConnection connection, CharacterEntity character, ICreature npc, uint questId)
    {
        try
        {
            if (NpcInteraction.RootFor(world.Data.Dialogue, npc.Metadata.Id) is not { } root)
                return;

            connection.CurrentDialogue = (npc.Guid, root.Id);
            InteractHandler.Send(connection, npc, root, character, world.Data, DialogueOptionsFor(connection, character, npc, root));
        }
        catch (Exception e)
        {
            if (!Throttled(_rootResendErrors, questId, out int suppressed))
                logger.LogError(e, "Re-sending NPC {Npc}'s root after quest {QuestId} changed for character {CharacterId} failed; " +
                                   "the request still succeeded. {Suppressed} earlier failures for this quest were not logged",
                    npc.Metadata.Id.Value, questId, character.Guid.Id, suppressed);
        }
    }

    /// <summary>
    /// A QuestFlusher step threw (final review M2): logged at Error at most once per step per
    /// <see cref="ThrottledErrorLog.Interval" />, counting the throws left out; the flush goes on with its next step.
    /// </summary>
    internal void FlushStepFailed(string step, Exception e)
    {
        if (!_flushStepErrors.TryGetValue(step, out ThrottledErrorLog? log))
            _flushStepErrors[step] = log = new ThrottledErrorLog(logger, time, step);
        log.Failed(e);
    }
}
