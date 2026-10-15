using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.World.Public;

namespace Avalon.World.Chat;

/// <summary>
/// What a chat command runs with (spec 2026-09-30 section 5). A command runs on the tick, to completion;
/// work it cannot finish there goes through <see cref="Then{T}" />, whose callback runs on a later tick.
/// A faulted or cancelled task and a callback that throws are all reported through the dispatcher,
/// exactly as a command that throws: logged at Error, and told to GameMaster and Admin only. Every line it sends is
/// stamped with the server's clock (<see cref="Now" />, #944), never with the time the client put on its message.
/// </summary>
public sealed class CommandContext
{
    // Where a failure goes: the callback this was built with, or the dispatcher that ran the command.
    private readonly Action<Exception>? _onFailure;
    private readonly CommandDispatcher? _dispatcher;
    private readonly ICommand? _command;

    /// <summary>A context of its own, outside the dispatcher: its lines are stamped with the system clock.</summary>
    public CommandContext(IWorldConnection connection, CChatMessagePacket packet, Action<Exception> onFailure)
    {
        Connection = connection;
        Packet = packet;
        _onFailure = onFailure;
    }

    /// <summary>
    /// The dispatcher's own: a failure is reported through it, for <paramref name="command" />. It names the two
    /// where it used to take a callback, which cost every command a closure and a delegate (#883).
    /// </summary>
    internal CommandContext(IWorldConnection connection, CChatMessagePacket packet, CommandDispatcher dispatcher, ICommand command)
    {
        Connection = connection;
        Packet = packet;
        _dispatcher = dispatcher;
        _command = command;
    }

    public IWorldConnection Connection { get; }

    public CChatMessagePacket Packet { get; }

    /// <summary>
    /// The stamp of a line sent now (#944): the container's clock (the dispatcher's), in UTC. The time on
    /// <see cref="Packet" /> is the client's, and nothing reads it.
    /// </summary>
    /// <remarks>
    /// Read through the dispatcher rather than held: every command that runs makes a context, and a field more would be
    /// eight bytes more on each.
    /// </remarks>
    public DateTime Now => (_dispatcher?.Time ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <summary>A system line to the caller.</summary>
    public void Reply(string message) =>
        Connection.Send(SChatMessagePacket.System(message, Now, PacketEncoder.Shared));

    /// <summary>Runs <paramref name="callback" /> on the tick once <paramref name="task" /> has succeeded.</summary>
    public void Then<T>(Task<T> task, Action<T> callback) =>
        Connection.EnqueueContinuation(Settled(task), () =>
        {
            if (!task.IsCompletedSuccessfully)
            {
                Failed(FailureOf(task));
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
                Failed(e);
            }
        });

    /// <summary>Runs <paramref name="callback" /> on the tick once <paramref name="task" /> has succeeded.</summary>
    public void Then(Task task, Action callback) =>
        Connection.EnqueueContinuation(Settled(task), () =>
        {
            if (!task.IsCompletedSuccessfully)
            {
                Failed(FailureOf(task));
                return;
            }

            try
            {
                callback();
            }
            catch (Exception e)
            {
                Failed(e);
            }
        });

    private void Failed(Exception failure)
    {
        if (_dispatcher is not null)
            _dispatcher.ReportFailure(Connection, Packet, _command!, failure);
        else
            _onFailure!(failure);
    }

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
