using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Microsoft.Win32.SafeHandles;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// One loop at 60 Hz sending each in-world bot's next input, as the game client's fixed step does: idle bots a zero
/// direction, walkers and churners their <see cref="Walker"/>'s heading, fighters what their <see cref="Fighter"/>
/// decides, with the one packet besides it a step may bring (an entry to a map, a cast, a respawn). Steps are planned on
/// a <see cref="Stopwatch"/> schedule from the start and recorded late by how far the step began after its planned time.
/// </summary>
/// <remarks>
/// The loop runs on its own thread and sleeps towards each step with a high-resolution waitable timer on Windows
/// (<see cref="Task.Delay(TimeSpan)"/> and <see cref="Thread.Sleep(int)"/> wake on the 15.6 ms system tick there, about
/// a whole step late; the world server schedules its tick the same way), <see cref="Thread.Sleep(TimeSpan)"/>
/// elsewhere, and yields through the last millisecond. Each step fires every bot's send without awaiting it, then
/// waits for the sends still in flight until the next step is due; a bot whose last send is still in flight then is
/// skipped until it completes, so one stalled socket never holds up the others. A fighter's packet besides its input
/// is sent the same way, and the fighter decides on another only once it has gone. Per bot and step it allocates the
/// packet and nothing else unless the send has to wait.
/// </remarks>
public sealed class InputDriver(Func<IReadOnlyCollection<Bot>> inWorld)
{
    public const int StepsPerSecond = 60;

    /// <summary>A walker or fighter whose ack reports less than 0.1 m/s while it asked to move was stopped (a wall).</summary>
    private const float BlockedSpeedSquared = 0.01f;

    /// <summary>Fallen further behind than this, the loop drops the steps it missed rather than send them in a burst.</summary>
    private const int MaxStepsBehind = 3;

    /// <summary>The most lateness samples a window keeps (18 minutes of steps); past it, a uniform subset.</summary>
    private const int MaxSamples = 65_536;

    private static readonly double s_ticksPerStep = (double)Stopwatch.Frequency / StepsPerSecond;

    /// <summary>The last stretch before a step is yielded through rather than slept: a sleep can overshoot by about this much.</summary>
    private static readonly long s_spinTicks = Stopwatch.Frequency / 1000;

    private readonly Lock _windowLock = new();
    private readonly List<double> _lateness = new(MaxSamples);
    private readonly List<Task> _inFlight = [];
    private readonly Random _rng = new();
    private long _samplesSeen;

    /// <summary>Runs the loop on its own thread until <paramref name="ct"/> is cancelled; the task then completes.</summary>
    public Task RunAsync(CancellationToken ct)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Run(ct);
                done.TrySetResult();
            }
            catch (Exception error)
            {
                done.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "Input driver",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
        return done.Task;
    }

    /// <summary>The 95th percentile of step lateness in this window, in milliseconds; NaN before the first step.</summary>
    public double LatenessP95Ms()
    {
        double[] sorted;
        lock (_windowLock)
            sorted = [.. _lateness];
        if (sorted.Length == 0) return double.NaN;

        Array.Sort(sorted);
        return sorted[Math.Clamp((int)Math.Ceiling(0.95 * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    /// <summary>Starts a new lateness window.</summary>
    public void ResetWindow()
    {
        lock (_windowLock)
        {
            _lateness.Clear();
            _samplesSeen = 0;
        }
    }

    private void Run(CancellationToken ct)
    {
        using var sleeper = new StepSleeper();
        long start = Stopwatch.GetTimestamp();
        for (long step = 0; !ct.IsCancellationRequested; step++)
        {
            long planned = start + (long)(step * s_ticksPerStep);
            sleeper.WaitUntil(planned);
            Record(Stopwatch.GetTimestamp() - planned);

            Step(ct);
            long nextPlanned = start + (long)((step + 1) * s_ticksPerStep);
            WaitForSends(nextPlanned);

            long now = Stopwatch.GetTimestamp();
            long behind = (long)((now - nextPlanned) / s_ticksPerStep);
            if (behind > MaxStepsBehind)
            {
                // Each dropped step is a sample as late as it now is, so a stall shows in the percentiles by its length.
                for (long dropped = 1; dropped <= behind; dropped++)
                    Record(now - (start + (long)((step + dropped) * s_ticksPerStep)));
                step += behind;
            }
        }
    }

    /// <summary>Builds and fires one input for every bot in the world whose previous send has completed.</summary>
    private void Step(CancellationToken ct)
    {
        long now = Stopwatch.GetTimestamp();
        foreach (Bot bot in inWorld())
        {
            if (bot.State != BotState.InWorld) continue;

            InputLane lane = bot.Lane;
            if (lane.Pending is { } pending)
            {
                if (!pending.IsCompleted) continue;
                // A failed send is the connection's end, which the bot's own loop sees and recovers from.
                _ = pending.Exception;
                lane.Pending = null;
            }

            // A new connection (seqs restarted at 1, the character spawned afresh): what the lane knew of the old one
            // no longer applies.
            int generation = bot.ConnectionGeneration;
            if (generation != lane.Generation)
            {
                lane.Moving = false;
                lane.Generation = generation;
                bot.Fighter?.Reset();
            }

            uint seq = bot.NextSeq();

            float dirX = 0f;
            float dirZ = 0f;
            FighterStep fighting = default;
            if (bot.Behaviour == BehaviourKind.Idle)
            {
                lane.Moving = false;
            }
            else
            {
                BotAck ack = bot.LastAck;
                // Blocked only by an ack answering an input on the current heading: acks still in flight from before a
                // turn report the old heading's wall.
                bool blocked = lane.Moving && ack.Seq >= lane.HeadingFrom &&
                    ack.VelX * ack.VelX + ack.VelZ * ack.VelZ < BlockedSpeedSquared;
                bool turned;
                if (bot.Fighter is { } fighter)
                {
                    fighting = fighter.Step(ack, blocked, ActionGone(lane), bot.CharacterGuid, seq, now, _rng);
                    (dirX, dirZ, lane.Yaw, turned) = (fighting.DirX, fighting.DirZ, fighting.Yaw, fighting.NewHeading);
                }
                else
                {
                    lane.Walker ??= new Walker();
                    (dirX, dirZ, lane.Yaw) = lane.Walker.Next(blocked, _rng);
                    turned = blocked;
                }

                // A fighter stands still at times: then nothing it asks for can be blocked.
                bool moving = dirX != 0f || dirZ != 0f;
                if (moving && (turned || !lane.Moving)) lane.HeadingFrom = seq;
                lane.Moving = moving;
            }

            ValueTask send;
            try
            {
                NetworkPacket packet = bot.NextInput(seq, dirX, dirZ, lane.Yaw);
                send = bot.SendAsync(packet, ct);
            }
            catch (InvalidOperationException)
            {
                // The bot left the world since it was looked at.
                continue;
            }

            if (!send.IsCompletedSuccessfully)
            {
                Task task = send.AsTask();
                lane.Pending = task;
                _inFlight.Add(task);
            }

            if (fighting.Action != FighterAction.None) SendAction(bot, lane, fighting, ct);
        }
    }

    /// <summary>Whether the fighter's last packet besides its input has gone (or there was none), its lane then cleared.</summary>
    private static bool ActionGone(InputLane lane)
    {
        if (lane.Action is not { } action) return true;
        if (!action.IsCompleted) return false;

        // A failed send is the connection's end, as for the input.
        _ = action.Exception;
        lane.Action = null;
        return true;
    }

    /// <summary>Seals and fires the packet a fighter decided on besides its input, from the lane's reused messages.</summary>
    private void SendAction(Bot bot, InputLane lane, FighterStep step, CancellationToken ct)
    {
        ValueTask send;
        try
        {
            NetworkPacket packet = step.Action switch
            {
                FighterAction.EnterForest => bot.SealInWorld(lane.EnterMap(Fighter.ForestMapId), NetworkPacketType.CMSG_ENTER_MAP),
                FighterAction.LeaveForest => bot.SealInWorld(lane.EnterMap(Fighter.TownMapId), NetworkPacketType.CMSG_ENTER_MAP),
                FighterAction.Cast => bot.SealInWorld(lane.Cast(bot.Fighter!.AbilityId, step.AimX, step.AimY, step.AimZ),
                    NetworkPacketType.CMSG_CAST_ABILITY),
                _ => bot.SealInWorld(InputLane.Respawn, NetworkPacketType.CMSG_RESPAWN_AT_TOWN),
            };
            send = bot.SendAsync(packet, ct);
        }
        catch (InvalidOperationException)
        {
            // The bot left the world since it was looked at.
            return;
        }

        if (send.IsCompletedSuccessfully) return;

        Task task = send.AsTask();
        lane.Action = task;
        _inFlight.Add(task);
    }

    /// <summary>Waits for this step's sends still in flight, at most until <paramref name="deadline"/>.</summary>
    private void WaitForSends(long deadline)
    {
        if (_inFlight.Count == 0) return;

        try
        {
            TimeSpan left = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
            if (left > TimeSpan.Zero) Task.WhenAll(_inFlight).Wait(left);
        }
        catch (AggregateException)
        {
            // Failed sends are left to their lanes (see Step).
        }
        finally
        {
            _inFlight.Clear();
        }
    }

    private void Record(long lateTicks)
    {
        double ms = Math.Max(0, lateTicks) * 1000.0 / Stopwatch.Frequency;
        lock (_windowLock)
        {
            long seen = _samplesSeen++;
            if (seen < MaxSamples)
            {
                _lateness.Add(ms);
                return;
            }

            long slot = _rng.NextInt64(seen + 1);
            if (slot < MaxSamples) _lateness[(int)slot] = ms;
        }
    }

    /// <summary>Sleeps towards a <see cref="Stopwatch"/> timestamp and yields through its last millisecond.</summary>
    private sealed class StepSleeper : IDisposable
    {
        private readonly SafeWaitHandle? _timer = OperatingSystem.IsWindows() ? HighResolutionTimer.TryCreate() : null;

        public void WaitUntil(long timestamp)
        {
            for (long left = timestamp - Stopwatch.GetTimestamp(); left > 0; left = timestamp - Stopwatch.GetTimestamp())
            {
                if (left <= s_spinTicks)
                {
                    Thread.Yield();
                    continue;
                }

                long sleep = left - s_spinTicks;
                if (_timer is not null && OperatingSystem.IsWindows())
                    HighResolutionTimer.Wait(_timer, sleep);
                else
                    Thread.Sleep(TimeSpan.FromSeconds((double)sleep / Stopwatch.Frequency));
            }
        }

        public void Dispose() => _timer?.Dispose();
    }

    /// <summary>A waitable timer that wakes within about half a millisecond (Windows 10 1803 and later).</summary>
    [SupportedOSPlatform("windows")]
    private static class HighResolutionTimer
    {
        private const uint CreateWaitableTimerHighResolution = 0x00000002;
        private const uint TimerAllAccess = 0x1F0003;
        private const uint Infinite = 0xFFFFFFFF;

        /// <summary>The timer, or null where the system has none (the loop then sleeps the coarse way).</summary>
        public static SafeWaitHandle? TryCreate()
        {
            SafeWaitHandle timer = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
            if (!timer.IsInvalid) return timer;

            timer.Dispose();
            return null;
        }

        /// <summary>Blocks for <paramref name="stopwatchTicks"/>.</summary>
        public static void Wait(SafeWaitHandle timer, long stopwatchTicks)
        {
            // In 100 ns units; negative is relative to now.
            long due = -(long)(stopwatchTicks * (10_000_000.0 / Stopwatch.Frequency));
            if (due == 0) return;
            if (SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                _ = WaitForSingleObject(timer, Infinite);
            else
                Thread.Sleep(1);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completion,
            IntPtr completionArgument, [MarshalAs(UnmanagedType.Bool)] bool resume);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    }
}

/// <summary>What the input driver keeps of one bot between steps; touched by the driver's thread only.</summary>
internal sealed class InputLane
{
    /// <summary>The walker's heading, made on the bot's first walking step.</summary>
    public Walker? Walker;

    /// <summary>The yaw last sent; an idle bot keeps it.</summary>
    public ushort Yaw;

    /// <summary>Whether the last input asked to move.</summary>
    public bool Moving;

    /// <summary>The first seq sent on the current heading.</summary>
    public uint HeadingFrom;

    /// <summary>The bot's <see cref="Bot.ConnectionGeneration"/> the lane last drove, to see a new connection.</summary>
    public int Generation;

    /// <summary>The last send, while it had not completed when fired.</summary>
    public Task? Pending;

    /// <summary>A fighter's last packet besides its input, while it had not completed when fired.</summary>
    public Task? Action;

    /// <summary>A respawn says nothing but that it is one.</summary>
    public static readonly CRespawnAtTownPacket Respawn = new();

    private CEnterMapPacket? _enterMap;
    private CCastAbilityPacket? _cast;

    /// <summary>The fighter's entry message, reused: a message is serialized when it is sealed, on this thread.</summary>
    public CEnterMapPacket EnterMap(ushort mapId)
    {
        _enterMap ??= new CEnterMapPacket();
        _enterMap.TargetMapId = mapId;
        return _enterMap;
    }

    /// <summary>The fighter's cast message, reused, aimed at the ground point given.</summary>
    public CCastAbilityPacket Cast(uint abilityId, float x, float y, float z)
    {
        _cast ??= new CCastAbilityPacket { GroundPos = new Vector3Dto() };
        _cast.AbilityId = abilityId;
        _cast.GroundPos!.X = x;
        _cast.GroundPos.Y = y;
        _cast.GroundPos.Z = z;
        return _cast;
    }
}
