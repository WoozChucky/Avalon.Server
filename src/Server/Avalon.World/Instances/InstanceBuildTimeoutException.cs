using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.World.Public;

namespace Avalon.World.Instances;

/// <summary>
/// The failure every requester of an instance build is handed when the build ran past
/// <see cref="InstanceRegistry.BuildDeadline" /> (#969): the registry abandoned it, on the tick, and the next request
/// starts a new one. The registry has logged it at Error and counted it, so a requester ending its move with it logs
/// no more than a Warning, and tells the player once (<see cref="TellIfTimedOut" />).
/// </summary>
public sealed class InstanceBuildTimeoutException : TimeoutException
{
    /// <summary>The system line the player whose move waited on the build is sent, once, as the move ends.</summary>
    public const string PlayerMessage = "The way could not be opened. Please try again.";

    /// <summary>
    /// The line instead when the server tries the move again itself: a party leave countdown's return to town, which is
    /// retried (#700, owner decision).
    /// </summary>
    public const string RetryingMessage = "The way could not be opened. Trying again…";

    public InstanceBuildTimeoutException(MapTemplateId templateId, TimeSpan deadline, DateTime timedOutAt)
        : base($"The build of an instance of map {templateId.Value} did not finish within {deadline.TotalSeconds:0} s; it was abandoned")
    {
        TemplateId = templateId;
        TimedOutAt = timedOutAt;
    }

    /// <summary>The map whose build timed out.</summary>
    public MapTemplateId TemplateId { get; }

    /// <summary>When the registry abandoned the build, by the world's clock (UTC): the time the player's line carries.</summary>
    public DateTime TimedOutAt { get; }

    /// <summary>
    /// The timeout <paramref name="failure" /> is, or is the one exception of (a faulted task's
    /// <see cref="Task.Exception" />), or null when it is any other failure.
    /// </summary>
    public static InstanceBuildTimeoutException? Of(Exception? failure) => failure switch
    {
        InstanceBuildTimeoutException timeout => timeout,
        AggregateException { InnerExceptions.Count: 1 } single => single.InnerExceptions[0] as InstanceBuildTimeoutException,
        _ => null,
    };

    /// <summary>
    /// Sends the connection's player <see cref="PlayerMessage" />, or <see cref="RetryingMessage" /> when
    /// <paramref name="retrying" />, as a system line when <paramref name="failure" /> is a build timeout, and says
    /// whether it was one. On the tick, by the requester that ends the move, once: a character has one move under way
    /// at a time.
    /// </summary>
    /// <param name="retrying">The server tries the move again itself (a party leave countdown's return to town).</param>
    public static bool TellIfTimedOut(IWorldConnection connection, Exception? failure, bool retrying = false)
    {
        if (Of(failure) is not { } timeout)
            return false;

        // A move that is given up is a refusal (#1249); one the server tries again is news.
        DateTime at = timeout.TimedOutAt;
        connection.Send(retrying
            ? SChatMessagePacket.System(RetryingMessage, at, PacketEncoder.Shared)
            : SChatMessagePacket.Refusal(PlayerMessage, at, PacketEncoder.Shared));
        return true;
    }
}
