using System.Diagnostics;

namespace Avalon.World.Threading;

/// <summary>
/// The tick-thread assertion (#639). World-side state that only the tick may change (instance membership, the
/// registry's indexes, parties, who is online, ignore lists) calls <see cref="AssertOnTick" /> before changing it.
/// <c>WorldServer</c>'s tick loop binds its thread when it starts and unbinds it when it ends; while it is bound, a
/// call from any other thread throws <see cref="InvalidOperationException" /> naming the operation.
/// </summary>
/// <remarks>
/// <para>
/// A check of the thread, not of a phase: today every serial step and every instance tick run on the one tick
/// thread, so the thread is the whole invariant. A phase (serial step versus instance tick) only matters once
/// instances tick in parallel, which is not built.
/// </para>
/// <para>
/// Debug builds only, which is what the tests run: <see cref="AssertOnTick" /> is <c>[Conditional("DEBUG")]</c>, so a
/// Release build compiles every call away, arguments included, and the hot paths pay nothing. It throws rather than
/// calling <c>Debug.Assert</c>, which ends the whole process (a test host included) instead of failing one call.
/// </para>
/// <para>
/// Unbound, nothing is refused: before the tick starts (the world load), after it ends (the shutdown despawn, which
/// runs on the host's thread once the tick has been joined), and in tests that drive the world from their own thread
/// without binding one. One per container (a DI singleton), so tests running side by side never share a binding.
/// </para>
/// </remarks>
public sealed class TickThreadGuard
{
    private int _tickThreadId;

    /// <summary>The calling thread is the tick from now on.</summary>
    public void Bind() => Volatile.Write(ref _tickThreadId, Environment.CurrentManagedThreadId);

    /// <summary>The tick has ended. Only the bound thread can unbind; any other call changes nothing.</summary>
    public void Unbind() => Interlocked.CompareExchange(ref _tickThreadId, 0, Environment.CurrentManagedThreadId);

    /// <summary>Throws when a tick is bound and this is not its thread. Compiled only into Debug builds.</summary>
    /// <param name="operation">What was called, for the message: <c>World.TransferPlayer</c>, say.</param>
    [Conditional("DEBUG")]
    public void AssertOnTick(string operation)
    {
        int tick = Volatile.Read(ref _tickThreadId);
        if (tick != 0 && tick != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                $"{operation} runs on the tick thread only (thread {tick}); it was called from thread " +
                $"{Environment.CurrentManagedThreadId}.");
        }
    }
}
