namespace Avalon.LoadTest.Bots;

/// <summary>Where a fighter check's trips stand (<see cref="CheckTrips.Verdict"/>).</summary>
public enum CheckTripsVerdict
{
    /// <summary>A member has trips to go.</summary>
    Pending,

    /// <summary>Every member completed a trip: it walked out of the forest into town.</summary>
    Passed,

    /// <summary>A member's trip failed (<see cref="TripEnd.Failed"/>).</summary>
    Failed,

    /// <summary>A member died on each of its <see cref="CheckTrips.MostTrips"/> trips, and none failed.</summary>
    DiedThrice,
}

/// <summary>
/// The rule of a fighter check's trips: a member whose trip ended in a death goes again (it has respawned in town, which
/// the check wanted to see work too), up to <see cref="MostTrips"/> trips. A member is done once a trip completed, once
/// one failed, or after its last trip; done, it stands in town. The check passes once every member completed a trip.
/// </summary>
/// <remarks>
/// <see cref="Ended"/> and <see cref="MaySetOut"/> run on the input driver's thread (where a fighter's trip ends and its
/// gate is asked); <see cref="Verdict"/> is read once <see cref="AllDone"/> has completed, or after the driver stopped.
/// </remarks>
public sealed class CheckTrips
{
    /// <summary>The most trips a member makes.</summary>
    public const int MostTrips = 3;

    private readonly int[] _trips;
    private readonly TripEnd?[] _done;
    private readonly TaskCompletionSource _allDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _doneCount;

    public CheckTrips(int members)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(members, 1);
        _trips = new int[members];
        _done = new TripEnd?[members];
    }

    /// <summary>Completes once every member is done.</summary>
    public Task AllDone => _allDone.Task;

    /// <summary>
    /// How the members' trips stand: a failure first (it names what failed), then a member that died on each of its
    /// trips, then pending while a member is not done, and passed when every member completed one.
    /// </summary>
    public CheckTripsVerdict Verdict
    {
        get
        {
            if (_done.Contains(TripEnd.Failed)) return CheckTripsVerdict.Failed;
            if (_done.Contains(TripEnd.Died)) return CheckTripsVerdict.DiedThrice;
            return _doneCount < _done.Length ? CheckTripsVerdict.Pending : CheckTripsVerdict.Passed;
        }
    }

    /// <summary>Whether <paramref name="member"/> may set out on a trip: it is not done.</summary>
    public bool MaySetOut(int member) => _done[member] is null;

    /// <summary>How many trips <paramref name="member"/> has ended.</summary>
    public int Trips(int member) => _trips[member];

    /// <summary>A trip of <paramref name="member"/> ended; returns whether it goes again (it died with trips left).</summary>
    public bool Ended(int member, TripEnd end)
    {
        if (_done[member] is not null) return false;

        _trips[member]++;
        if (end == TripEnd.Died && _trips[member] < MostTrips) return true;

        _done[member] = end;
        if (++_doneCount == _done.Length) _allDone.TrySetResult();
        return false;
    }
}
