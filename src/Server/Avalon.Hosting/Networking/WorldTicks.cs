namespace Avalon.Hosting.Networking;

/// <summary>
/// The world's tick count (#1066): how many ticks of the world's loop have completed. The tick writes it as each tick
/// ends; the state broadcast reads it on the tick, so each add and update names the tick that built it
/// (<c>SInstanceStateAddPacket.WorldTick</c>, <c>SInstanceStateUpdatePacket.WorldTick</c>), and each send thread reads it
/// as it stamps a time-sync ping, beside the ping's server time (<c>SPingPacket.WorldTick</c>), so a client can turn a
/// broadcast's tick into server time. One per world, owned by its <see cref="NetworkSendScheduler" />
/// (<see cref="NetworkSendScheduler.Ticks" />), so the pings and the tick read the same count.
/// </summary>
/// <remarks>
/// One writer, the tick, and a 64-bit field read with <see cref="Volatile" />: a plain load, never torn, with no lock and
/// no interlocked operation, on the tick as on a send thread. A tick that throws is not counted, so the tick after it
/// carries the same number.
/// </remarks>
public sealed class WorldTicks
{
    /// <summary>
    /// The rate the world's loop is scheduled at, in ticks a second (#1066), which every time-sync ping states
    /// (<c>SPingPacket.WorldTickRate</c>). A tick that runs long, or the loop's resync after it falls behind, leaves the
    /// count short of this rate's: a client turns a tick into server time from the ping's anchor, never from tick 0.
    /// </summary>
    public const int PerSecond = 60;

    private long _completed;

    /// <param name="completed">The count to start from: 0 for a world, more for a test that needs a long uptime.</param>
    public WorldTicks(long completed = 0) => _completed = completed;

    /// <summary>
    /// The ticks completed. Read during a tick, the number of that tick (the first is 0); read between ticks, the number of
    /// the next. Any thread.
    /// </summary>
    public long Completed => Volatile.Read(ref _completed);

    /// <summary>One more tick completed. The tick only, as each tick ends.</summary>
    public void Advance() => Volatile.Write(ref _completed, _completed + 1);
}
