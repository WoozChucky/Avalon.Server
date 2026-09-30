using Avalon.Network.Packets.Social;
using Avalon.World.Public;

namespace Avalon.World.Chat;

/// <summary>
/// What a chat command runs with (spec 2026-09-30 section 5). A command runs on the tick, to completion;
/// work it cannot finish there goes through <see cref="Then{T}" />, whose callback runs on a later tick.
/// A faulted or cancelled task and a callback that throws are all reported through the dispatcher,
/// exactly as a command that throws: logged at Error, and told to GameMaster and Admin only.
/// </summary>
public sealed class CommandContext(IWorldConnection connection, CChatMessagePacket packet, Action<Exception> onFailure)
{
    public IWorldConnection Connection { get; } = connection;

    public CChatMessagePacket Packet { get; } = packet;

    /// <summary>A system line to the caller.</summary>
    public void Reply(string message) =>
        Connection.Send(SChatMessagePacket.Create(0UL, 0UL, "System", message, Packet.DateTime,
            Connection.CryptoSession.Encrypt));

    /// <summary>Runs <paramref name="callback" /> on the tick once <paramref name="task" /> has succeeded.</summary>
    public void Then<T>(Task<T> task, Action<T> callback) =>
        Connection.EnqueueContinuation(Settled(task), () =>
        {
            if (!task.IsCompletedSuccessfully)
            {
                onFailure(FailureOf(task));
                return;
            }

            try
            {
#pragma warning disable MA0045 // the task has completed successfully: reading its result does not block
                callback(task.Result);
#pragma warning restore MA0045
            }
            catch (Exception e)
            {
                onFailure(e);
            }
        });

    /// <summary>Runs <paramref name="callback" /> on the tick once <paramref name="task" /> has succeeded.</summary>
    public void Then(Task task, Action callback) =>
        Connection.EnqueueContinuation(Settled(task), () =>
        {
            if (!task.IsCompletedSuccessfully)
            {
                onFailure(FailureOf(task));
                return;
            }

            try
            {
                callback();
            }
            catch (Exception e)
            {
                onFailure(e);
            }
        });

    /// <summary>
    /// A task that completes when <paramref name="task" /> does and never faults, so the connection's
    /// continuation drain always runs the callback above (it logs and drops a faulted task's callback).
    /// </summary>
    private static Task Settled(Task task) =>
        task.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static Exception FailureOf(Task task) =>
        task.Exception is { InnerExceptions.Count: 1 } single ? single.InnerExceptions[0]
        : task.Exception is { } many ? many
        : new TaskCanceledException(task);
}
