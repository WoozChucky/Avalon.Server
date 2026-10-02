namespace Avalon.World.Threading;

/// <summary>
/// The tick-thread assertion (#639). World-side state that only the tick may change (instance membership, the
/// registry's indexes, parties, who is online, ignore lists) calls <see cref="AssertOnTick" /> before changing it.
/// <c>WorldServer</c>'s tick loop binds its thread when it starts and unbinds it when it ends; while the check is
/// <see cref="Enabled" /> and a thread is bound, a call from any other thread throws
/// <see cref="InvalidOperationException" /> naming the operation.
/// </summary>
/// <remarks>
/// <para>
/// A check of the thread, not of a phase: today every serial step and every instance tick run on the one tick
/// thread, so the thread is the whole invariant. A phase (serial step versus instance tick) only matters once
/// instances tick in parallel, which is not built.
/// </para>
/// <para>
/// Compiled into every build and checked only while <see cref="Enabled" /> (owner decision): <c>Game:TickThreadGuard</c>
/// turns it on when the world server starts (development), and the test assembly turns it on for itself. Off, each
/// check costs one read of a static volatile flag. It throws rather than calling <c>Debug.Assert</c>, which ends the
/// whole process (a test host included) instead of failing one call.
/// </para>
/// <para>
/// Unbound, nothing is refused: before the tick starts (the world load), after it ends (the shutdown despawn, which
/// runs on the host's thread once the tick has been joined), and in tests that drive the world from their own thread
/// without binding one. The binding is per instance, one per container (a DI singleton), so tests running side by
/// side never share one; only the on/off switch is process-wide.
/// </para>
/// </remarks>
public sealed class TickThreadGuard
{
    private static volatile bool s_enabled;

    private int _tickThreadId;

    /// <summary>
    /// Whether any guard checks. Process-wide; only ever turned on (by the world server at start when
    /// <c>Game:TickThreadGuard</c> is set, or by a test assembly), so a host that leaves it off cannot turn it off for
    /// anything already relying on it.
    /// </summary>
    public static bool Enabled => s_enabled;

    /// <summary>Turns every guard's check on for the rest of the process.</summary>
    public static void Enable() => s_enabled = true;

    /// <summary>The calling thread is the tick from now on.</summary>
    public void Bind() => Volatile.Write(ref _tickThreadId, Environment.CurrentManagedThreadId);

    /// <summary>The tick has ended. Only the bound thread can unbind; any other call changes nothing.</summary>
    public void Unbind() => Interlocked.CompareExchange(ref _tickThreadId, 0, Environment.CurrentManagedThreadId);

    /// <summary>Throws when the check is enabled, a tick is bound, and this is not its thread.</summary>
    /// <param name="operation">What was called, for the message: <c>World.TransferPlayer</c>, say.</param>
    public void AssertOnTick(string operation)
    {
        if (!s_enabled)
            return;

        int tick = Volatile.Read(ref _tickThreadId);
        if (tick != 0 && tick != Environment.CurrentManagedThreadId)
            Refuse(operation, tick);
    }

    private static void Refuse(string operation, int tick) =>
        throw new InvalidOperationException(
            $"{operation} runs on the tick thread only (thread {tick}); it was called from thread " +
            $"{Environment.CurrentManagedThreadId}.");
}
