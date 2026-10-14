using System.Numerics;
using Avalon.Common;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts.Creatures;

namespace Avalon.World.Creatures;

/// <summary>
/// Which of an instance's creatures run their script on a creature step (#979), so a creature that only waits costs
/// the tick nothing. One per <c>MapInstance</c>; tick thread only, like the instance's creatures (#639).
/// </summary>
/// <remarks>
/// <para>
/// Every creature with a script is in one of the states of <see cref="CreatureWait" />: active (runs every step, as every
/// creature did before; a script that answers <see cref="AiScript.NextUpdateIn" /> zero stays so), waiting for its range detector's search, waiting for a time on the instance's creature clock, or
/// waiting for an event. After each Update its script says which (<see cref="AiScript.NextUpdateIn" />, or, for a
/// built-in that only waits for its search, <see cref="IAwaitsSearch" />); the creature is woken (<see cref="Wake" />)
/// by its due time, by every event the instance delivers to its script (a hit, an attack that dealt nothing, a
/// character leaving), by a new script or patrol path, and by joining the instance. Every activation (joining, a new
/// script, a wake, a due search or time) goes through <see cref="MakeActive" />, and every wait begins in
/// <see cref="Park" />, so a rule that holds or wakes a creature (crowd control, #770) has one place for each; reading
/// the roster again (<see cref="ReadRoster" />) changes no state, it only sets each place's bit from it.
/// </para>
/// <para>
/// <b>Order.</b> The creatures that run on a step run in the order of the instance's creature dictionary, the order
/// the every-tick loop walks, so which creature claims a melee slot, hits first or kills first is unchanged. The
/// roster (<see cref="_roster" />) is that order, read again from the dictionary on the first step after a creature
/// joined; a creature that leaves only empties its place. Which places run is a bit set per step.
/// </para>
/// <para>
/// <b>The search wait.</b> A built-in creature's range detector searches once its timer, a <see cref="float" /> of
/// seconds that each tick adds its delta to, reaches <see cref="CreatureRangeDetectorScript.SearchInterval" />. Waiting
/// creatures whose timers hold the same value are kept together in a cohort, whose one timer each step advances
/// exactly as each detector's own would have been, the same float sum in the same order; on the step the timer would
/// reach the interval the cohort's creatures run, each with its detector's timer put back to the cohort's, and search
/// on that tick as they did when every creature was visited. Creatures placed together, or searching on the same
/// tick, share a cohort, so a step costs a few float additions however many creatures wait.
/// </para>
/// </remarks>
internal sealed class CreatureScheduler
{
    private const int CohortCapacity = 16;

    private readonly Dictionary<ObjectGuid, ICreature> _creatures;

    // The instance's creatures in dictionary order, a null where one has left since the roster was read.
    private ICreature?[] _roster = [];
    private int _rosterCount;
    private bool _rosterStale;

    // Per roster place: runs on every step (_every), and this step (_now).
    private ulong[] _every = [];
    private ulong[] _now = [];

    // The roster place whose script is running, or -1 outside the step's loop.
    private int _running = -1;

    private TimeSpan _clock;

    private SearchCohort[] _cohorts = new SearchCohort[CohortCapacity];
    private int[] _freeCohorts = new int[CohortCapacity];
    private int _freeCohortCount;
    private int _cohortCount;

    // The cohort the last creature joined, tried first: the creatures searching on one tick all join the same.
    private int _lastJoined = -1;

    // Creatures waiting for a time: a binary min-heap on their due times, each holding its slot in it.
    private Creature?[] _timed = new Creature?[CohortCapacity];
    private int _timedCount;

    public CreatureScheduler(Dictionary<ObjectGuid, ICreature> creatures)
    {
        _creatures = creatures;
    }

    /// <summary>
    /// A creature joined the instance: it runs on the next step, whatever its script declares, and the roster is read
    /// again then. One that is not the World-side type (a test's) runs on every step.
    /// </summary>
    public void Joined(ICreature creature)
    {
        _rosterStale = true;

        // Room for every creature now, here rather than on the step: an instance's creatures join as it is built, off the
        // tick, so its first tick allocates nothing.
        EnsureRoom(_creatures.Count);
        if (creature is not Creature scheduled)
            return;

        scheduled.Scheduler?.Left(scheduled);
        scheduled.Schedule = default;
        scheduled.Schedule.Index = -1;
        scheduled.Scheduler = this;
        Register(scheduled, runNow: false);
    }

    /// <summary>A creature left the instance: it is no longer run, and holds no place, cohort or wait.</summary>
    public void Left(ICreature creature)
    {
        if (creature is not Creature scheduled)
        {
            // Not schedulable, so not tracked by place: the roster is read again on the next step.
            _rosterStale = true;
            return;
        }

        if (!ReferenceEquals(scheduled.Scheduler, this))
            return;

        Leave(scheduled);
        int index = scheduled.Schedule.Index;
        if (index >= 0 && index < _rosterCount && ReferenceEquals(_roster[index], scheduled))
        {
            _roster[index] = null;
            Clear(_every, index);
            Clear(_now, index);
        }

        scheduled.Schedule = default;
        scheduled.Schedule.Index = -1;
        scheduled.Scheduler = null;
    }

    /// <summary>Its creature has a new script (a kill takes it away): taken up at once, from the creature's next turn.</summary>
    public void ScriptChanged(Creature creature)
    {
        if (!ReferenceEquals(creature.Scheduler, this))
            return;

        Leave(creature);
        Register(creature, runNow: RunsLaterThisStep(creature.Schedule.Index));
    }

    /// <summary>
    /// Wakes a waiting creature: it runs on the next step, or on this one when the step has yet to reach it, as the
    /// every-tick loop would have run it. A search waiter's detector gets back the timer it would have had. A creature
    /// that is not waiting is left as it is.
    /// </summary>
    public void Wake(Creature creature)
    {
        if (!ReferenceEquals(creature.Scheduler, this))
            return;

        switch (creature.Schedule.Wait)
        {
            case CreatureWait.Search:
            {
                bool runNow = RunsLaterThisStep(creature.Schedule.Index);
                ref SearchCohort cohort = ref _cohorts[creature.Schedule.Cohort];

                // Not reached by this step's loop yet: it has not had this tick, so it gets the timer from before it.
                creature.Schedule.Search!.ResumeSearchWait(runNow ? cohort.Before : cohort.Elapsed);
                LeaveCohort(creature);
                MakeActive(creature, runNow);
                break;
            }

            case CreatureWait.Timed:
                LeaveTimed(creature);
                MakeActive(creature, RunsLaterThisStep(creature.Schedule.Index));
                break;

            case CreatureWait.Event:
                MakeActive(creature, RunsLaterThisStep(creature.Schedule.Index));
                break;
        }
    }

    /// <summary>
    /// The instance's creature step (#979): the clock is the instance's creature clock with this tick's delta in it.
    /// Every cohort's timer and the timed waits first, then each creature that runs, in the instance's order.
    /// </summary>
    public void Step(TimeSpan deltaTime, TimeSpan clock)
    {
        _clock = clock;
        if (_rosterStale)
            ReadRoster();

        int words = Words(_rosterCount);
        Array.Copy(_every, _now, words);

        AdvanceCohorts((float)deltaTime.TotalSeconds);
        WakeDueTimes();

        try
        {
            for (int w = 0; w < words; w++)
            {
                // Read again after every script: one may wake a creature later in this word.
                while (_now[w] != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(_now[w]);
                    _now[w] &= ~(1UL << bit);
                    _running = (w << 6) | bit;
                    Run(_roster[_running], deltaTime);
                }
            }
        }
        finally
        {
            _running = -1;
        }
    }

    /// <summary>How many creatures run on every step now: the active ones. For tests and benchmarks.</summary>
    public int EveryStep
    {
        get
        {
            int count = 0;
            for (int w = 0; w < Words(_rosterCount); w++)
                count += BitOperations.PopCount(_every[w]);
            return count;
        }
    }

    private void Run(ICreature? creature, TimeSpan deltaTime)
    {
        if (creature is not Creature scheduled)
        {
            // A creature that is not the World-side type (a test's) cannot be scheduled: it runs every step.
            creature?.Script?.Update(deltaTime);
            return;
        }

        AiScript? script = scheduled.Script;
        if (script is null)
            return;

        script.Update(deltaTime);

        // Unless the script left, lost or replaced its creature, or something it did woke or parked it, it says what it
        // waits for now.
        if (ReferenceEquals(scheduled.Scheduler, this) && ReferenceEquals(scheduled.Script, script)
            && scheduled.Schedule.Wait == CreatureWait.Active)
        {
            Park(scheduled, script);
        }
    }

    /// <summary>A creature that has just run waits for whatever its script says, or stays active.</summary>
    private void Park(Creature creature, AiScript script)
    {
        // A built-in with a search wait answers for itself: it waits for its search, or it is active (IAwaitsSearch), so
        // its NextUpdateIn, which asks the same question, is not read. Only a script whose NextUpdateIn is that built-in's
        // own has a search wait (Register).
        if (creature.Schedule.Search is { } search)
        {
            if (search.AwaitsSearch(out float elapsed))
                JoinCohort(creature, elapsed);
            return;
        }

        TimeSpan wait = script.NextUpdateIn;

        // A wait past the end of the clock (TimeSpan.MaxValue, say) is a wait for an event, not an overflow.
        if (wait == Timeout.InfiniteTimeSpan || wait >= TimeSpan.MaxValue - _clock)
        {
            creature.Schedule.Wait = CreatureWait.Event;
            ClearPlace(creature);
        }
        else if (wait > TimeSpan.Zero)
        {
            JoinTimed(creature, _clock + wait);
        }
    }

    /// <summary>
    /// The one way a creature comes to run (#979): joining, a new script, a wake, a due time. It runs from the next step,
    /// and on this one too when <paramref name="runNow" />. A rule that holds a creature (crowd control, #770) belongs
    /// here.
    /// </summary>
    private void MakeActive(Creature creature, bool runNow)
    {
        creature.Schedule.Wait = CreatureWait.Active;
        int index = creature.Schedule.Index;
        if (index < 0)
            return; // Not in the roster yet: reading it marks the creature to run.

        Set(_every, index);
        if (runNow)
            Set(_now, index);
    }

    private void Register(Creature creature, bool runNow)
    {
        AiScript? script = creature.Script;
        creature.Schedule.Search = null;
        if (script is null)
        {
            creature.Schedule.Wait = CreatureWait.None;
            ClearPlace(creature);
            return;
        }

        creature.Schedule.Search = script is IAwaitsSearch { HasSearchWait: true } search ? search : null;
        MakeActive(creature, runNow);
    }

    /// <summary>Takes the creature out of whatever it waits in, leaving its state for the caller to set.</summary>
    private void Leave(Creature creature)
    {
        switch (creature.Schedule.Wait)
        {
            case CreatureWait.Search:
                LeaveCohort(creature);
                break;
            case CreatureWait.Timed:
                LeaveTimed(creature);
                break;
        }
    }

    private bool RunsLaterThisStep(int index) => _running >= 0 && index > _running;

    private void ClearPlace(Creature creature)
    {
        int index = creature.Schedule.Index;
        if (index >= 0)
        {
            Clear(_every, index);
            Clear(_now, index);
        }
    }

    // ---- The search wait ----------------------------------------------------------------------------------------

    private void AdvanceCohorts(float seconds)
    {
        _lastJoined = -1;
        for (int c = 0; c < _cohortCount; c++)
        {
            ref SearchCohort cohort = ref _cohorts[c];
            if (cohort.Head is null)
                continue;

            // CreatureRangeDetectorScript.AdvanceWhileWaiting, to the bit: the same float sum, and due where it says due.
            float next = cohort.Elapsed + seconds;
            if (next < CreatureRangeDetectorScript.SearchInterval)
            {
                cohort.Before = cohort.Elapsed;
                cohort.Elapsed = next;
                continue;
            }

            // Due: every creature in it searches on this tick, its timer as it stood before the tick. The cohort is let go
            // whole, in one pass over its list, rather than member by member.
            float elapsed = cohort.Elapsed;
            Creature? member = cohort.Head;
            cohort.Head = null;
            FreeCohort(c);
            while (member is not null)
            {
                Creature? following = member.Schedule.Next;
                member.Schedule.Search!.ResumeSearchWait(elapsed);
                member.Schedule.Cohort = -1;
                member.Schedule.Previous = null;
                member.Schedule.Next = null;
                MakeActive(member, runNow: true);
                member = following;
            }
        }
    }

    private void JoinCohort(Creature creature, float elapsed)
    {
        int found = -1;
        if (_lastJoined >= 0 && _cohorts[_lastJoined].Head is not null && _cohorts[_lastJoined].Elapsed == elapsed)
        {
            found = _lastJoined;
        }
        else
        {
            for (int c = 0; c < _cohortCount; c++)
            {
                if (_cohorts[c].Head is not null && _cohorts[c].Elapsed == elapsed)
                {
                    found = c;
                    break;
                }
            }
        }

        if (found < 0)
            found = NewCohort(elapsed);

        ref SearchCohort cohort = ref _cohorts[found];
        creature.Schedule.Wait = CreatureWait.Search;
        creature.Schedule.Cohort = found;
        creature.Schedule.Previous = null;
        creature.Schedule.Next = cohort.Head;
        if (cohort.Head is not null)
            cohort.Head.Schedule.Previous = creature;
        cohort.Head = creature;
        _lastJoined = found;
        ClearPlace(creature);
    }

    private void LeaveCohort(Creature creature)
    {
        int c = creature.Schedule.Cohort;
        ref SearchCohort cohort = ref _cohorts[c];
        Unlink(creature, ref cohort.Head);
        creature.Schedule.Cohort = -1;
        creature.Schedule.Wait = CreatureWait.Active;
        if (cohort.Head is null)
            FreeCohort(c);
    }

    /// <summary>An emptied cohort, free for the next to take: cohorts are kept, never allocated per tick.</summary>
    private void FreeCohort(int c)
    {
        if (_freeCohortCount == _freeCohorts.Length)
            Array.Resize(ref _freeCohorts, _freeCohorts.Length * 2);
        _freeCohorts[_freeCohortCount++] = c;
        if (_lastJoined == c)
            _lastJoined = -1;
    }

    private int NewCohort(float elapsed)
    {
        int c;
        if (_freeCohortCount > 0)
        {
            c = _freeCohorts[--_freeCohortCount];
        }
        else
        {
            if (_cohortCount == _cohorts.Length)
                Array.Resize(ref _cohorts, _cohorts.Length * 2);
            c = _cohortCount++;
        }

        // Before is read only for a creature this step has yet to reach, and a cohort made this step holds none.
        _cohorts[c] = new SearchCohort { Elapsed = elapsed, Before = elapsed };
        return c;
    }

    // ---- The timed wait -----------------------------------------------------------------------------------------

    private void JoinTimed(Creature creature, TimeSpan due)
    {
        creature.Schedule.Wait = CreatureWait.Timed;
        creature.Schedule.Due = due;
        if (_timedCount == _timed.Length)
            Array.Resize(ref _timed, _timed.Length * 2); // Grows to the most that ever wait at once, then stays.

        int at = _timedCount++;
        _timed[at] = creature;
        creature.Schedule.Slot = at;
        SiftUp(at);
        ClearPlace(creature);
    }

    private void LeaveTimed(Creature creature)
    {
        int at = creature.Schedule.Slot;
        int last = --_timedCount;
        if (at != last)
        {
            Creature moved = _timed[last]!;
            _timed[at] = moved;
            moved.Schedule.Slot = at;
            SiftDown(at);
            SiftUp(moved.Schedule.Slot);
        }

        _timed[last] = null;
        creature.Schedule.Slot = -1;
        creature.Schedule.Wait = CreatureWait.Active;
    }

    /// <summary>Wakes every timed waiter whose time has come: O(log n) for each, nothing for those still waiting.</summary>
    private void WakeDueTimes()
    {
        while (_timedCount > 0 && _timed[0]!.Schedule.Due <= _clock)
        {
            Creature creature = _timed[0]!;
            LeaveTimed(creature);
            MakeActive(creature, runNow: true);
        }
    }

    private void SiftUp(int at)
    {
        Creature creature = _timed[at]!;
        while (at > 0)
        {
            int parent = (at - 1) >> 1;
            Creature above = _timed[parent]!;
            if (above.Schedule.Due <= creature.Schedule.Due)
                break;

            _timed[at] = above;
            above.Schedule.Slot = at;
            at = parent;
        }

        _timed[at] = creature;
        creature.Schedule.Slot = at;
    }

    private void SiftDown(int at)
    {
        Creature creature = _timed[at]!;
        while (true)
        {
            int child = (at << 1) + 1;
            if (child >= _timedCount)
                break;

            if (child + 1 < _timedCount && _timed[child + 1]!.Schedule.Due < _timed[child]!.Schedule.Due)
                child++;

            Creature below = _timed[child]!;
            if (creature.Schedule.Due <= below.Schedule.Due)
                break;

            _timed[at] = below;
            below.Schedule.Slot = at;
            at = child;
        }

        _timed[at] = creature;
        creature.Schedule.Slot = at;
    }

    private static void Unlink(Creature creature, ref Creature? head)
    {
        Creature? previous = creature.Schedule.Previous;
        Creature? next = creature.Schedule.Next;
        if (previous is not null)
            previous.Schedule.Next = next;
        else
            head = next;
        if (next is not null)
            next.Schedule.Previous = previous;
        creature.Schedule.Previous = null;
        creature.Schedule.Next = null;
    }

    // ---- The roster ---------------------------------------------------------------------------------------------

    /// <summary>Reads the roster again from the instance's dictionary: its order, each creature's place, and who runs.</summary>
    private void ReadRoster()
    {
        int count = _creatures.Count;
        EnsureRoom(count);
        Array.Clear(_roster, 0, _rosterCount);
        Array.Clear(_every, 0, Words(_rosterCount));

        int index = 0;
        foreach (ICreature creature in _creatures.Values)
        {
            _roster[index] = creature;
            if (creature is not Creature scheduled || !ReferenceEquals(scheduled.Scheduler, this))
            {
                Set(_every, index); // Run every step, as the every-tick loop did.
            }
            else
            {
                scheduled.Schedule.Index = index;
                if (scheduled.Schedule.Wait is CreatureWait.Active)
                    Set(_every, index);
            }

            index++;
        }

        _rosterCount = count;
        _rosterStale = false;
    }

    /// <summary>
    /// Room in the roster and its bit sets for <paramref name="count" /> creatures, with some to spare so creatures joining
    /// one by one (summons) do not grow it each time. Only a roster about to be read again is grown: its places are
    /// all assigned anew then.
    /// </summary>
    private void EnsureRoom(int count)
    {
        if (_roster.Length >= count)
            return;

        int capacity = Math.Max(64, (int)BitOperations.RoundUpToPowerOf2((uint)count));
        Array.Resize(ref _roster, capacity);
        Array.Resize(ref _every, Words(capacity));
        Array.Resize(ref _now, Words(capacity));
    }

    private static int Words(int places) => (places + 63) >> 6;

    private static void Set(ulong[] bits, int index) => bits[index >> 6] |= 1UL << (index & 63);

    private static void Clear(ulong[] bits, int index)
    {
        if (index >> 6 < bits.Length)
            bits[index >> 6] &= ~(1UL << (index & 63));
    }

    /// <summary>Waiting creatures whose range detectors' timers hold the same value, and that value.</summary>
    private struct SearchCohort
    {
        public Creature? Head;

        /// <summary>The timer now: after this step's tick once the step has advanced it.</summary>
        public float Elapsed;

        /// <summary>The timer before this step's tick, for a creature woken before this step reached it.</summary>
        public float Before;
    }
}

/// <summary>What a creature's schedule waits for (#979). See <see cref="CreatureScheduler" />.</summary>
internal enum CreatureWait : byte
{
    /// <summary>Not scheduled: no script, or not in an instance that schedules.</summary>
    None,

    /// <summary>Active: it runs on every step until its script says it waits.</summary>
    Active,

    /// <summary>Waiting for its range detector's search, in a cohort.</summary>
    Search,

    /// <summary>Waiting for a time on the creature clock.</summary>
    Timed,

    /// <summary>Waiting for an event alone.</summary>
    Event,
}

/// <summary>A creature's place in its instance's <see cref="CreatureScheduler" />. Held by the creature, so it allocates nothing.</summary>
internal struct CreatureSchedule
{
    public CreatureWait Wait;

    /// <summary>Its place in the scheduler's roster, or -1 before the roster is read.</summary>
    public int Index;

    /// <summary>The cohort it waits in, while it waits for its search.</summary>
    public int Cohort;

    /// <summary>The creature time it waits for, while it waits for one.</summary>
    public TimeSpan Due;

    /// <summary>Its script's search wait, when its script is a built-in that has one.</summary>
    public IAwaitsSearch? Search;

    /// <summary>Its slot in the timed waits' heap, while it waits for a time.</summary>
    public int Slot;

    /// <summary>The creatures before and after it in its cohort, while it waits for its search.</summary>
    public Creature? Previous;
    public Creature? Next;
}
