namespace Avalon.World;

/// <summary>
/// A connection's continuation queue for work whose every outcome must be answered on the tick. The ordinary
/// continuation (<c>IWorldConnection.EnqueueContinuation</c>) runs its callback only when its task succeeded: for a
/// task that faulted or was cancelled the drain logs the fault and drops the callback. That is right for a callback
/// that only uses a result, and wrong for one that must also end something the request started (a flag, a wait, a
/// reply owed): dropped, it never does. Wrapping the task in one that cannot fault gets the callback run, but costs a
/// task per request and hands the outcome to a thread-pool thread before the tick sees it. An entry queued here
/// costs what an ordinary one does, and is ready the moment its task is.
/// </summary>
/// <remarks>
/// Lives in Avalon.World, outside Avalon.World.Public, as <see cref="ICharacterLeaveControl" /> does: the modding
/// API's connection keeps its two continuations, and the server's own (<see cref="WorldConnection" />) has this one
/// beside them, in the same queue.
/// </remarks>
public interface ISettledContinuations
{
    /// <summary>
    /// Runs <paramref name="callback" /> on the tick once <paramref name="task" /> has ended, however it ended
    /// (succeeded, faulted or cancelled), and hands it the task: the callback reads the outcome itself, and nothing
    /// is logged for a task that failed unless the callback logs it. Never before the task has ended. It is queued
    /// behind the connection's continuations already waiting and drained with them, so it runs in the first flush
    /// that finds its task ended; a callback that throws is contained and logged as any other continuation's is.
    /// </summary>
    void EnqueueSettled<T>(Task<T> task, Action<Task<T>> callback);
}
