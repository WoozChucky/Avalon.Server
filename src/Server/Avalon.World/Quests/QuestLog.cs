using System.Diagnostics;
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

    // The copies the walks over the held quests go through, one per walk in progress, kept and emptied after each
    // so that a walk allocates no copy (#883). See BeginWalk.
    private readonly List<List<ActiveQuest>> _walks = [];
    private int _walking;

    // The client changes in quest id order, filled again for each flush: quest ids and kinds, nothing that lives.
    private readonly List<(uint QuestId, QuestClientChange Change)> _clientById = [];

    /// <summary>
    /// The quests held. The dictionary's own collection, so a <c>foreach</c> over it uses its struct enumerator and
    /// allocates nothing (#883); it is the log itself, not a copy, so a walk that may settle a quest or run a script's
    /// hook goes through <see cref="BeginWalk" /> instead.
    /// </summary>
    public Dictionary<uint, ActiveQuest>.ValueCollection Active => _active.Values;

    public IReadOnlyCollection<uint> Completed => _completed.Keys;

    public int ActiveCount => _active.Count;

    /// <summary>
    /// Raised by every change a quest marker can see, so the markers know when to look again (#973): which quests are
    /// held or turned in (an accept, an abandon, a turn-in, a load) and a held quest's state (ready to turn in or not).
    /// A count or a stage that leaves the state as it is leaves it alone: a marker reads neither (see
    /// <see cref="QuestService.MarkerFor" />), and a crediting kill or a Bag recount would otherwise make every
    /// character it touched walk every creature of its instance for a list equal to the one sent.
    /// </summary>
    public int MarkerVersion { get; private set; }

    /// <summary>
    /// Raised only when which quests are held or turned in changes (an accept, an abandon, a turn-in, a load), the two
    /// things a vendor's quest gate asks (#738), so the vendor pass knows when an open shop's list may have changed.
    /// Progress and stages leave it alone.
    /// </summary>
    public int HeldVersion { get; private set; }

    public ActiveQuest? Get(uint questId) => _active.GetValueOrDefault(questId);

    /// <summary>
    /// The quests held now, copied into a list that stays as it is while the caller walks it. A walk that credits a
    /// kill, a conversation or the Bag settles quests and runs their scripts' hooks, and whatever those do to the log,
    /// the walk goes over the quests held when it began, each once: one accepted meanwhile is not part of it, and one
    /// dropped meanwhile is still walked and found gone by the caller's own checks.
    /// </summary>
    /// <remarks>
    /// The list is this log's, so a walk allocates no copy once it has grown (the copy used to be a <c>ToList()</c>
    /// per walk, #883), and the caller gives it back with <see cref="EndWalk" /> in a <c>finally</c>. One list per
    /// walk in progress: a walk begun from inside another (a hook that leads to one) gets its own, so neither moves
    /// under the other. Per character and never shared, since a character's log is only ever walked by its own tick.
    /// </remarks>
    public List<ActiveQuest> BeginWalk()
    {
        if (_walking == _walks.Count)
            _walks.Add([]);

        List<ActiveQuest> held = _walks[_walking++];
        Debug.Assert(held.Count == 0, "A list of quests to walk still holds an earlier walk's");
        foreach (ActiveQuest quest in _active.Values)
            held.Add(quest);
        return held;
    }

    /// <summary>Empties the list <see cref="BeginWalk" /> handed out, so the log keeps no copy between walks, and frees its depth.</summary>
    public void EndWalk(List<ActiveQuest> held)
    {
        held.Clear();
        _walking--;
    }

    public bool IsActive(uint questId) => _active.ContainsKey(questId);

    public bool IsCompleted(uint questId) => _completed.ContainsKey(questId);

    public DateTime? CompletedAt(uint questId) => _completed.TryGetValue(questId, out DateTime at) ? at : null;

    /// <summary>The quests whose client update is owed, and which update. QuestFlusher sends and clears them.</summary>
    public IReadOnlyDictionary<uint, QuestClientChange> ClientChanges => _client;

    /// <summary>
    /// <see cref="ClientChanges" /> by quest id, the order the updates are sent in. A list of this log's, filled again
    /// by each call and sorted in place: a query that ordered the changes allocated for every flush that had any
    /// (#883). No two entries share an id, so the order is the same whatever the sort does with ties.
    /// </summary>
    public List<(uint QuestId, QuestClientChange Change)> ClientChangesById()
    {
        _clientById.Clear();
        foreach (KeyValuePair<uint, QuestClientChange> owed in _client)
            _clientById.Add((owed.Key, owed.Value));

        _clientById.Sort(static (a, b) => a.QuestId.CompareTo(b.QuestId));
        return _clientById;
    }

    /// <summary>System lines owed to the character (milestones), in order. QuestFlusher sends and clears them.</summary>
    public IReadOnlyList<string> PendingLines => _lines;

    /// <summary>True once this session's SMSG_QUEST_LOG has gone out. Never saved.</summary>
    public bool LogSent { get; set; }

    /// <summary>The locale this session's lines are written in; set at select from the connection. Never saved.</summary>
    public Avalon.Common.Accounts.AccountLocale Locale { get; set; }

    // The markers last sent (QuestFlusher), and what they were computed from (MarkersVersion is the MarkerVersion they
    // were worked out at). Never saved.
    public Guid? MarkersInstance { get; set; }
    public ushort MarkersLevel { get; set; }
    public int MarkersVersion { get; set; } = -1;
    public object? MarkersCatalog { get; set; }
    public IReadOnlyList<(ulong Creature, byte Marker)>? MarkersSent { get; set; }

    /// <summary>
    /// The quest catalog generation this session's held quests were last settled against (#738): set by the select
    /// recount, and when QuestFlusher sees a /reload quests landed. Null until then. Never saved.
    /// </summary>
    public object? SettledCatalog { get; set; }

    /// <summary>The instance the OnEnterInstance hooks last ran for. Never saved.</summary>
    public Guid? ScriptsInstance { get; set; }

    /// <summary>
    /// Whether the next quest flush has anything of this character's to do (#977). Set by everything that changes what
    /// the flush sends or settles from the character: every change to the log (<see cref="Start" />,
    /// <see cref="SetProgress" />, <see cref="SetStage" />, <see cref="SetState" />, <see cref="Remove" />,
    /// <see cref="Complete" />), every line (<see cref="Say" />), a <see cref="Load" />, and the character's instance
    /// and level (the hooks' and the markers' inputs; <c>CharacterEntity</c>'s setters mark them as they change, so an
    /// instance or a level written to the row directly is not seen). Set to begin with, so a new session's first flush
    /// sends the log. Cleared by QuestFlusher once a flush leaves nothing for a later one. What a flush reads from
    /// elsewhere it checks itself: the catalog's generation (a /reload quests, one for every character) and the Bag's
    /// slot changes (the inventory's). Never saved.
    /// </summary>
    public bool FlushOwed { get; internal set; } = true;

    /// <summary>Something a quest flush reads from the character changed (#977): see <see cref="FlushOwed" />.</summary>
    internal void MarkFlushOwed() => FlushOwed = true;

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

        HeldVersion++;
        MarkerVersion++;
        FlushOwed = true;
    }

    public ActiveQuest Start(uint questId, DateTime now)
    {
        var quest = new ActiveQuest(questId, now);
        _active[questId] = quest;
        HeldVersion++;
        MarkerVersion++;
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
        MarkerVersion++;
        Changed(quest.QuestId, QuestClientChange.Progress);
    }

    /// <summary>An abandon: the quest and its progress are gone, and it can be accepted again.</summary>
    public bool Remove(uint questId)
    {
        if (!_active.Remove(questId))
            return false;

        HeldVersion++;
        MarkerVersion++;
        Changed(questId, QuestClientChange.Removed);
        return true;
    }

    /// <summary>
    /// A turn-in: the quest leaves the active set and joins the completed one. A quest already completed keeps its
    /// first completion time, as the saved row does.
    /// </summary>
    public void Complete(uint questId, DateTime now)
    {
        _active.Remove(questId);
        _completed.TryAdd(questId, now);
        HeldVersion++;
        MarkerVersion++;
        Changed(questId, QuestClientChange.Completed);
    }

    public void Say(string line)
    {
        _lines.Add(line);
        FlushOwed = true;
    }

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
        {
            _client[questId] = change;
        }

        save.QuestChanged(questId);
        FlushOwed = true;
    }
}
