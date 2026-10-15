namespace Avalon.World.GameAuth;

/// <summary>
/// Whether identity may hold a session past the lease the world holds (#1051), which decides whether the session's end
/// may be skipped once that lease ran out. A connection keeps one <see cref="int" /> state, written on the tick only: a
/// heartbeat started is <see cref="InFlight" />, and its renewal applied to the lease makes it <see cref="Settled" />
/// again. A heartbeat that ended any other way (an error, a transport failure or timeout, a reply the lease refused, a
/// task that faulted) leaves its outcome at identity unknown: identity may have renewed the session, and a request it
/// still holds may renew it later, after a later heartbeat was applied. That makes the state <see cref="Unknown" /> for
/// the rest of the session, and its end is always sent. Static, over a field of the connection: nothing is allocated.
/// </summary>
public static class RenewalLedger
{
    public const int Settled = 0;
    public const int InFlight = 1;
    public const int Unknown = 2;

    /// <summary>A heartbeat was started.</summary>
    public static void Started(ref int state)
    {
        if (Volatile.Read(ref state) != Unknown) Volatile.Write(ref state, InFlight);
    }

    /// <summary>The heartbeat's renewal was applied to the lease.</summary>
    public static void Applied(ref int state)
    {
        if (Volatile.Read(ref state) == InFlight) Volatile.Write(ref state, Settled);
    }

    /// <summary>The heartbeat ended without its renewal applied: what identity did with it is not known.</summary>
    public static void Unresolved(ref int state) => Volatile.Write(ref state, Unknown);

    /// <summary>Whether identity may hold the session past the lease held here.</summary>
    public static bool MayOutlastLease(ref int state) => Volatile.Read(ref state) != Settled;
}
