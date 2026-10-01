using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.World.Persistence;

namespace Avalon.World.Quests;

/// <summary>What the client must hear about a quest at the end of the tick. At most one per quest per tick.</summary>
public enum QuestClientChange : byte
{
    Progress = 1,
    Accepted = 2,
    Removed = 3,
    Completed = 4,
}

/// <summary>One active quest of one character. Changed only through its <see cref="QuestLog" />.</summary>
public sealed class ActiveQuest(uint questId, DateTime acceptedAt)
{
    private readonly Dictionary<uint, uint> _progress = [];

    public uint QuestId { get; } = questId;
    public CharacterQuestState State { get; internal set; } = CharacterQuestState.Active;
    public int Stage { get; internal set; }
    public DateTime AcceptedAt { get; } = acceptedAt;

    /// <summary>Every objective with a count above 0, by objective id.</summary>
    public IReadOnlyDictionary<uint, uint> Progress => _progress;

    public uint ProgressOf(uint objectiveId) => _progress.GetValueOrDefault(objectiveId);

    internal bool Set(uint objectiveId, uint value)
    {
        if (ProgressOf(objectiveId) == value)
            return false;

        if (value == 0)
            _progress.Remove(objectiveId);
        else
            _progress[objectiveId] = value;
        return true;
    }
}

/// <summary>
/// One character's quests (#433): the active ones, with their stage and objective counts, and the ids it has turned
/// in. World-side and tick-thread only, like the inventory. Every change marks the save
/// (<see cref="SaveStateTracker.QuestChanged" />) and what the client must hear; <see cref="Load" /> marks neither.
/// It knows nothing about the catalog: QuestService decides what a change means and caps every count. The
/// mutators are public only because the tests cannot see internals; QuestService is their one caller.
/// </summary>
public sealed class QuestLog(SaveStateTracker save)
{
    private readonly Dictionary<uint, ActiveQuest> _active = [];
    private readonly Dictionary<uint, DateTime> _completed = [];
    private readonly Dictionary<uint, QuestClientChange> _client = [];
    private readonly List<string> _lines = [];

    public IReadOnlyCollection<ActiveQuest> Active => _active.Values;

    public IReadOnlyCollection<uint> Completed => _completed.Keys;

    public int ActiveCount => _active.Count;

    /// <summary>Raised by every change, so the markers know when to look again.</summary>
    public int Version { get; private set; }

    public ActiveQuest? Get(uint questId) => _active.GetValueOrDefault(questId);

    public bool IsActive(uint questId) => _active.ContainsKey(questId);

    public bool IsCompleted(uint questId) => _completed.ContainsKey(questId);

    public DateTime? CompletedAt(uint questId) => _completed.TryGetValue(questId, out DateTime at) ? at : null;

    /// <summary>The quests whose client update is owed, and which update. QuestFlusher sends and clears them.</summary>
    public IReadOnlyDictionary<uint, QuestClientChange> ClientChanges => _client;

    /// <summary>System lines owed to the character (milestones), in order. QuestFlusher sends and clears them.</summary>
    public IReadOnlyList<string> PendingLines => _lines;

    /// <summary>True once this session's SMSG_QUEST_LOG has gone out. Never saved.</summary>
    public bool LogSent { get; set; }

    /// <summary>The locale this session's lines are written in; set at select from the connection. Never saved.</summary>
    public Avalon.Common.Accounts.AccountLocale Locale { get; set; }

    // The markers last sent (QuestFlusher), and what they were computed from. Never saved.
    public Guid? MarkersInstance { get; set; }
    public ushort MarkersLevel { get; set; }
    public int MarkersVersion { get; set; } = -1;
    public object? MarkersCatalog { get; set; }
    public IReadOnlyList<(ulong Creature, byte Marker)>? MarkersSent { get; set; }

    /// <summary>The instance the OnEnterInstance hooks last ran for. Never saved.</summary>
    public Guid? ScriptsInstance { get; set; }

    /// <summary>Replaces the whole log at select. Marks nothing.</summary>
    public void Load(CharacterQuestRows rows)
    {
        _active.Clear();
        _completed.Clear();
        _client.Clear();
        _lines.Clear();

        foreach (CharacterQuest row in rows.Active)
            _active[row.QuestId] = new ActiveQuest(row.QuestId, row.AcceptedAt) { State = row.State, Stage = row.Stage };
        foreach (CharacterQuestObjective row in rows.Objectives)
        {
            if (_active.TryGetValue(row.QuestId, out ActiveQuest? quest))
                quest.Set(row.ObjectiveId, row.Progress);
        }

        foreach (CharacterCompletedQuest row in rows.Completed)
            _completed[row.QuestId] = row.CompletedAt;
    }

    public ActiveQuest Start(uint questId, DateTime now)
    {
        var quest = new ActiveQuest(questId, now);
        _active[questId] = quest;
        Changed(questId, QuestClientChange.Accepted);
        return quest;
    }

    /// <summary>False, marking nothing, when the objective already holds <paramref name="value" />.</summary>
    public bool SetProgress(ActiveQuest quest, uint objectiveId, uint value)
    {
        if (!quest.Set(objectiveId, value))
            return false;

        Changed(quest.QuestId, QuestClientChange.Progress);
        return true;
    }

    public void SetStage(ActiveQuest quest, int stage)
    {
        quest.Stage = stage;
        Changed(quest.QuestId, QuestClientChange.Progress);
    }

    public void SetState(ActiveQuest quest, CharacterQuestState state)
    {
        if (quest.State == state)
            return;

        quest.State = state;
        Changed(quest.QuestId, QuestClientChange.Progress);
    }

    /// <summary>An abandon: the quest and its progress are gone, and it can be accepted again.</summary>
    public bool Remove(uint questId)
    {
        if (!_active.Remove(questId))
            return false;

        Changed(questId, QuestClientChange.Removed);
        return true;
    }

    /// <summary>A turn-in: the quest leaves the active set and joins the completed one.</summary>
    public void Complete(uint questId, DateTime now)
    {
        _active.Remove(questId);
        _completed[questId] = now;
        Changed(questId, QuestClientChange.Completed);
    }

    public void Say(string line) => _lines.Add(line);

    public void ClearClientChanges()
    {
        _client.Clear();
        _lines.Clear();
    }

    private void Changed(uint questId, QuestClientChange change)
    {
        // An accept stays an accept for the rest of its tick: the client needs its display data.
        if (!(change == QuestClientChange.Progress && _client.TryGetValue(questId, out QuestClientChange owed)
              && owed == QuestClientChange.Accepted))
            _client[questId] = change;

        save.QuestChanged(questId);
        Version++;
    }
}
