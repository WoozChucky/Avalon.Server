using Avalon.World.Configuration;

namespace Avalon.World.Quests;

/// <summary>
/// The world's quest reset clock (#710): the latest daily, weekly and monthly boundary, and a generation raised once
/// whenever an <see cref="Advance(DateTime)" /> crosses one or more of them, however many it passed at once. Tick-thread
/// only. The boundaries are as of the last <see cref="Advance(DateTime)" />, which WorldServer calls once per tick, so
/// every offer, accept, marker and gate within a tick agrees with <see cref="Generation" />.
/// </summary>
public sealed class QuestResetClock
{
    private readonly QuestResetConfiguration _options;
    private readonly TimeProvider _time;
    private DateTime _daily;
    private DateTime _weekly;
    private DateTime _monthly;
    private int _dailyRolledAt;
    private int _weeklyRolledAt;
    private int _monthlyRolledAt;
    private int _generation;
    private DateTime _next;

    /// <summary>Reads <paramref name="time" /> once to set the three boundaries and the next one, at generation 0.</summary>
    public QuestResetClock(QuestResetConfiguration options, TimeProvider time)
    {
        _options = options;
        _time = time;
        DateTime utcNow = time.GetUtcNow().UtcDateTime;
        _daily = QuestResetSchedule.LastBoundary(QuestRepeat.Daily, utcNow, options);
        _weekly = QuestResetSchedule.LastBoundary(QuestRepeat.Weekly, utcNow, options);
        _monthly = QuestResetSchedule.LastBoundary(QuestRepeat.Monthly, utcNow, options);
        // Every weekly and monthly boundary is also a daily one, so the next daily boundary is the next of any.
        _next = QuestResetSchedule.NextBoundary(QuestRepeat.Daily, utcNow, options);
    }

    /// <summary>Raised once by every <see cref="Advance(DateTime)" /> that crossed a boundary; 0 until the first.</summary>
    public int Generation => _generation;

    /// <summary>The next boundary of any frequency: the first moment at which <see cref="Advance(DateTime)" /> moves.</summary>
    public DateTime NextBoundary => _next;

    /// <summary>The boundary of <paramref name="repeat" /> as of the last <see cref="Advance(DateTime)" />.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repeat" /> has no boundary (Once or Repeatable).</exception>
    public DateTime LastBoundary(QuestRepeat repeat) => repeat switch
    {
        QuestRepeat.Daily => _daily,
        QuestRepeat.Weekly => _weekly,
        QuestRepeat.Monthly => _monthly,
        _ => throw new ArgumentOutOfRangeException(nameof(repeat), repeat, "Only daily, weekly and monthly quests reset."),
    };

    /// <summary>
    /// The generation at which the boundary of <paramref name="repeat" /> last moved; 0 when it has not moved since the
    /// clock was built.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repeat" /> has no boundary (Once or Repeatable).</exception>
    public int RolledAt(QuestRepeat repeat) => repeat switch
    {
        QuestRepeat.Daily => _dailyRolledAt,
        QuestRepeat.Weekly => _weeklyRolledAt,
        QuestRepeat.Monthly => _monthlyRolledAt,
        _ => throw new ArgumentOutOfRangeException(nameof(repeat), repeat, "Only daily, weekly and monthly quests reset."),
    };

    /// <summary>Advances to the container's current time. True when it raised the generation.</summary>
    public bool Advance() => Advance(_time.GetUtcNow().UtcDateTime);

    /// <summary>
    /// Moves every boundary <paramref name="utcNow" /> has passed and raises the generation once, however many it
    /// passed. True when it raised the generation. Before the next boundary it costs one comparison.
    /// </summary>
    public bool Advance(DateTime utcNow)
    {
        if (utcNow < _next)
            return false;

        int generation = ++_generation;
        DateTime daily = QuestResetSchedule.LastBoundary(QuestRepeat.Daily, utcNow, _options);
        DateTime weekly = QuestResetSchedule.LastBoundary(QuestRepeat.Weekly, utcNow, _options);
        DateTime monthly = QuestResetSchedule.LastBoundary(QuestRepeat.Monthly, utcNow, _options);
        if (daily != _daily)
        {
            _daily = daily;
            _dailyRolledAt = generation;
        }

        if (weekly != _weekly)
        {
            _weekly = weekly;
            _weeklyRolledAt = generation;
        }

        if (monthly != _monthly)
        {
            _monthly = monthly;
            _monthlyRolledAt = generation;
        }

        _next = QuestResetSchedule.NextBoundary(QuestRepeat.Daily, utcNow, _options);
        return true;
    }

    /// <summary>
    /// Whether a quest of <paramref name="repeat" /> last turned in at <paramref name="lastCompletedAt" /> may be done
    /// again: Once never, Repeatable always, and a periodic quest when it was turned in before its latest boundary. A
    /// turn-in exactly at the boundary counts as after it.
    /// </summary>
    public bool AvailableAgain(QuestRepeat repeat, DateTime lastCompletedAt) => repeat switch
    {
        QuestRepeat.Once => false,
        QuestRepeat.Repeatable => true,
        _ => lastCompletedAt < LastBoundary(repeat),
    };

    /// <summary>
    /// Whether the owner of <paramref name="log" /> turned the quest in within the current period of its repeat kind:
    /// turned in, and not <see cref="AvailableAgain" />. For a once-only quest, and for one the catalog no longer has,
    /// that is "turned in at all"; a plain repeatable is never completed this period. What doodad gates ask, as against
    /// prerequisites and vendor gates, which ask <see cref="QuestLog.IsCompleted" />. Allocates nothing.
    /// </summary>
    public bool CompletedThisPeriod(QuestLog log, QuestCatalog quests, uint questId) =>
        log.Completion(questId) is { } done
        && !(quests.TryGet(questId, out QuestView? quest) && AvailableAgain(quest.Repeat, done.LastCompletedAt));
}
