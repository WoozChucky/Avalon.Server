using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Microsoft.Win32.SafeHandles;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// Sends each in-world bot's next input at 60 Hz, as the game client's fixed step does: idle bots a zero direction,
/// walkers and churners their <see cref="Walker"/>'s heading, fighters what their <see cref="Fighter"/> decides, with
/// the one packet besides it a step may bring (an entry to a map, a cast, a respawn). The bots are shared out over
/// several loops, each on its own thread with its own 60 Hz schedule; each bot is driven by one loop for its whole life
/// (<see cref="LoopAssigner"/>). Each loop plans its steps on a <see cref="Stopwatch"/> schedule from the driver's start,
/// its phase a fraction of a step after the loop before it, and records each step late by how far it began after its
/// planned time; the lateness of every loop goes into one window (<see cref="Lateness"/>). A loop that fails stops
/// every loop: the driver's task faults, and its lateness reads NaN from then on, so no window passes as judged while
/// some bots go without input.
/// </summary>
/// <remarks>
/// <para>
/// Why several loops: a bot's step is mostly its send, all of it on the loop's thread: the input serialized, sealed
/// when the world asked for it, framed, encrypted into one TLS record and written to the socket, whose send completes
/// at once while the socket has room. That is one record and one send per bot, which cannot be batched across bots
/// (each has its own connection), so one thread runs out of step time at some bot count while the PC has cores to
/// spare. Shared out, each loop does its part of the bots in its own step.
/// </para>
/// <para>
/// Each loop sleeps towards its next step with a high-resolution waitable timer on Windows
/// (<see cref="Task.Delay(TimeSpan)"/> and <see cref="Thread.Sleep(int)"/> wake on the 15.6 ms system tick there, about
/// a whole step late; the world server schedules its tick the same way), <see cref="Thread.Sleep(TimeSpan)"/>
/// elsewhere, and yields through the last millisecond. Each step fires every one of its bots' sends without awaiting
/// it, then waits for the sends still in flight until its next step is due; a bot whose last send is still in flight
/// then is skipped until it completes, so one stalled socket never holds up the others. A fighter's packet besides its
/// input is sent the same way, and the fighter decides on another only once it has gone. Per bot and step a loop
/// allocates the packet and nothing else unless the send has to wait. The loops' phases are spread over the step, as
/// real clients' steps are, so the world does not get every bot's input in one burst and the threads do not all wake
/// at one instant.
/// </para>
/// </remarks>
public sealed class InputDriver
{
    public const int StepsPerSecond = 60;

    /// <summary>The most loops a driver runs (<c>--driver-loops</c>).</summary>
    public const int MaxLoops = 64;

    /// <summary>A walker or fighter whose ack reports less than 0.1 m/s while it asked to move was stopped (a wall).</summary>
    private const float BlockedSpeedSquared = 0.01f;

    /// <summary>Fallen further behind than this, a loop drops the steps it missed rather than send them in a burst.</summary>
    private const int MaxStepsBehind = 3;

    /// <summary>The most lateness samples a window keeps (over two minutes of steps of 8 loops); past it, a uniform subset.</summary>
    private const int MaxSamples = 65_536;

    private static readonly double s_ticksPerStep = (double)Stopwatch.Frequency / StepsPerSecond;

    /// <summary>The last stretch before a step is yielded through rather than slept: a sleep can overshoot by about this much.</summary>
    private static readonly long s_spinTicks = Stopwatch.Frequency / 1000;

    private readonly Func<IReadOnlyCollection<Bot>> _inWorld;
    private readonly LoopAssigner _assigner;
    private readonly Lock _windowLock = new();
    private readonly List<double> _lateness = new(MaxSamples);

    /// <summary>The loop each of <see cref="_lateness"/>'s samples came from, at the same place.</summary>
    private readonly List<int> _latenessLoop = new(MaxSamples);
    private readonly long[] _busyTicks;
    private readonly long[] _busySteps;
    private long _samplesSeen;
    private volatile bool _failed;

    /// <param name="inWorld">The bots to drive; those not <see cref="BotState.InWorld"/> are passed over. Called by every loop.</param>
    /// <param name="loops">How many loops share the bots, 1 to <see cref="MaxLoops"/> (<see cref="DefaultLoops"/>).</param>
    public InputDriver(Func<IReadOnlyCollection<Bot>> inWorld, int loops)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(loops, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(loops, MaxLoops);
        _inWorld = inWorld;
        _assigner = new LoopAssigner(loops);
        _busyTicks = new long[loops];
        _busySteps = new long[loops];
    }

    /// <summary>
    /// The loops a driver runs unless told otherwise: 8, or the PC's logical cores when it has fewer. One loop held about
    /// 1,250 bots on the bot PC (an i9-12900K, 24 logical cores) before its steps started late, so 8 hold the most bots a
    /// run can have (5,000) with each loop at about half of that; each loop costs about 6 % of a core in the last
    /// millisecond it yields through before each step, whether it has bots or not.
    /// </summary>
    public static int DefaultLoops => Math.Min(8, Environment.ProcessorCount);

    /// <summary>How many loops share the bots.</summary>
    public int Loops => _assigner.Loops;

    /// <summary>
    /// Runs every loop on its own thread until <paramref name="ct"/> is cancelled; the task then completes. A loop that
    /// throws stops every loop within a step, and the task faults with what it threw: a loop that died alone would leave
    /// its bots without input, and the bots given to it after, while the other loops' lateness looked fine.
    /// </summary>
    public Task RunAsync(CancellationToken ct)
    {
        long start = Stopwatch.GetTimestamp();
        var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var loops = new Task[Loops];
        for (int index = 0; index < loops.Length; index++)
        {
            var loop = new Loop(this, index, start);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    loop.Run(stop.Token);
                    done.TrySetResult();
                }
                catch (Exception error)
                {
                    // Failed first, so the window reads NaN before any other loop stops; then every loop is stopped.
                    _failed = true;
                    done.TrySetException(error);
                    stop.Cancel();
                }
            })
            {
                IsBackground = true,
                Name = FormattableString.Invariant($"Input driver {index + 1}/{loops.Length}"),
                Priority = ThreadPriority.AboveNormal,
            };
            thread.Start();
            loops[index] = done.Task;
        }

        var all = Task.WhenAll(loops);
        _ = all.ContinueWith(_ => stop.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return all;
    }

    /// <summary>
    /// The 95th percentile of step lateness in this window over the steps of every loop together, in milliseconds (what
    /// <c>gen-lag</c> judges); NaN before the first step, and for good once a loop failed.
    /// </summary>
    public double LatenessP95Ms() => Lateness().P95Ms;

    /// <summary>
    /// This window's step lateness: the 95th percentile over the steps of every loop together, and the highest of the
    /// loops' own, so a single loop out of step time shows even where the others' steps outnumber its own; NaN before
    /// the first step, and for good once a loop failed.
    /// </summary>
    public DriverLateness Lateness()
    {
        double[] samples;
        int[] loops;
        lock (_windowLock)
        {
            samples = [.. _lateness];
            loops = [.. _latenessLoop];
        }

        if (_failed || samples.Length == 0) return new DriverLateness(double.NaN, double.NaN, Loops);

        double worst = double.NaN;
        for (int loop = 0; loop < Loops; loop++)
        {
            int index = loop;
            double[] own = [.. samples.Where((_, i) => loops[i] == index)];
            if (own.Length == 0) continue;

            double p95 = P95(own);
            if (!(worst >= p95)) worst = p95;
        }

        return new DriverLateness(P95(samples), worst, Loops);
    }

    /// <summary>The nearest-rank 95th percentile; sorts <paramref name="samples"/>, which are not empty.</summary>
    private static double P95(double[] samples)
    {
        Array.Sort(samples);
        return samples[Math.Clamp((int)Math.Ceiling(0.95 * samples.Length) - 1, 0, samples.Length - 1)];
    }

    /// <summary>
    /// How busy the loops were in this window: the mean time a loop spent per step deciding its bots' inputs and firing
    /// their sends (not waiting for sends in flight), as a share of a step (16.7 ms), averaged over the loops and for the
    /// busiest one. Near 1, a loop runs out of step time and its steps start late; low with lateness high, the lateness is
    /// not the loops' own work. Null before a loop's first step, and once a loop failed.
    /// </summary>
    public DriverBusy? Busy()
    {
        if (_failed) return null;

        double sum = 0;
        double busiest = 0;
        int counted = 0;
        lock (_windowLock)
        {
            for (int loop = 0; loop < _busyTicks.Length; loop++)
            {
                if (_busySteps[loop] == 0) continue;

                double share = _busyTicks[loop] / (_busySteps[loop] * s_ticksPerStep);
                sum += share;
                busiest = Math.Max(busiest, share);
                counted++;
            }
        }

        return counted == 0 ? null : new DriverBusy(sum / counted, busiest);
    }

    /// <summary>Starts a new window: lateness and busy time.</summary>
    public void ResetWindow()
    {
        lock (_windowLock)
        {
            _lateness.Clear();
            _latenessLoop.Clear();
            _samplesSeen = 0;
            Array.Clear(_busyTicks);
            Array.Clear(_busySteps);
        }
    }

    /// <summary>
    /// One step's lateness, in the window every loop shares, and, for a step the loop ran (not one it dropped), how long
    /// it was busy. <paramref name="rng"/> is the recording loop's own.
    /// </summary>
    private void Record(int loop, long lateTicks, long busyTicks, Random rng)
    {
        double ms = Math.Max(0, lateTicks) * 1000.0 / Stopwatch.Frequency;
        lock (_windowLock)
        {
            if (busyTicks >= 0)
            {
                _busyTicks[loop] += busyTicks;
                _busySteps[loop]++;
            }

            long seen = _samplesSeen++;
            if (seen < MaxSamples)
            {
                _lateness.Add(ms);
                _latenessLoop.Add(loop);
                return;
            }

            long slot = rng.NextInt64(seen + 1);
            if (slot < MaxSamples)
            {
                _lateness[(int)slot] = ms;
                _latenessLoop[(int)slot] = loop;
            }
        }
    }

    /// <summary>
    /// One of the driver's loops: its own thread, schedule, <see cref="Random"/> and sends in flight, and the bots
    /// <see cref="LoopAssigner"/> gave it. Everything here is touched by the loop's thread only.
    /// </summary>
    /// <remarks>
    /// A loop walks only its own bots and those no loop has yet (every loop holds those), not every bot: another loop's
    /// bot's lane is written by that loop every step, and reading it from here would cost a cache miss per bot per loop.
    /// The two lists are rebuilt from the bots only when the collection given changes (the ramp hands out the same one
    /// until a bot signs in); a bot no loop had is claimed once it is in the world, or dropped once another loop has it.
    /// </remarks>
    private sealed class Loop(InputDriver driver, int index, long start)
    {
        private readonly List<Task> _inFlight = [];
        private readonly Random _rng = new();
        private readonly List<Bot> _mine = [];
        private readonly List<Bot> _unclaimed = [];
        private IReadOnlyCollection<Bot>? _seen;

        /// <summary>Where this loop's steps fall in the driver's: <c>index / loops</c> of a step after the first loop's.</summary>
        private readonly long _phase = (long)(index * s_ticksPerStep / driver.Loops);

        public void Run(CancellationToken ct)
        {
            using var sleeper = new StepSleeper();
            long first = start + _phase;
            for (long step = 0; !ct.IsCancellationRequested; step++)
            {
                long planned = first + (long)(step * s_ticksPerStep);
                sleeper.WaitUntil(planned);
                long began = Stopwatch.GetTimestamp();

                Step(began, ct);
                long ended = Stopwatch.GetTimestamp();
                driver.Record(index, began - planned, ended - began, _rng);
                long nextPlanned = first + (long)((step + 1) * s_ticksPerStep);
                WaitForSends(nextPlanned);

                long now = Stopwatch.GetTimestamp();
                long behind = (long)((now - nextPlanned) / s_ticksPerStep);
                if (behind > MaxStepsBehind)
                {
                    // Each dropped step is a sample as late as it now is, so a stall shows in the percentiles by its length.
                    for (long dropped = 1; dropped <= behind; dropped++)
                        driver.Record(index, now - (first + (long)((step + dropped) * s_ticksPerStep)), -1, _rng);
                    step += behind;
                }
            }
        }

        /// <summary>
        /// Rebuilds the loop's own bots and the unclaimed ones when the collection given is a new one; allocates only
        /// then.
        /// </summary>
        private void Refresh()
        {
            IReadOnlyCollection<Bot> bots = driver._inWorld();
            if (ReferenceEquals(bots, _seen)) return;

            _seen = bots;
            _mine.Clear();
            _unclaimed.Clear();
            foreach (Bot bot in bots)
            {
                int owner = Volatile.Read(ref bot.Lane.Loop);
                if (owner == index) _mine.Add(bot);
                else if (owner == LoopAssigner.Unassigned) _unclaimed.Add(bot);
            }
        }

        /// <summary>
        /// Gives each unclaimed bot now in the world its loop, once, by whichever loop sees it first, and keeps it here if
        /// it is this loop's; a bot another loop has is dropped from the list, one not yet in the world kept.
        /// </summary>
        private void Claim()
        {
            for (int i = _unclaimed.Count - 1; i >= 0; i--)
            {
                Bot bot = _unclaimed[i];
                InputLane lane = bot.Lane;
                int owner = Volatile.Read(ref lane.Loop);
                if (owner == LoopAssigner.Unassigned)
                {
                    if (bot.State != BotState.InWorld) continue;

                    owner = driver._assigner.LoopOf(ref lane.Loop, bot.Behaviour);
                }

                if (owner == index) _mine.Add(bot);
                _unclaimed[i] = _unclaimed[^1];
                _unclaimed.RemoveAt(_unclaimed.Count - 1);
            }
        }

        /// <summary>Builds and fires one input for every bot of this loop in the world whose previous send has completed.</summary>
        private void Step(long now, CancellationToken ct)
        {
            Refresh();
            if (_unclaimed.Count > 0) Claim();

            for (int i = 0; i < _mine.Count; i++)
            {
                Bot bot = _mine[i];
                if (bot.State != BotState.InWorld) continue;

                InputLane lane = bot.Lane;
                if (lane.Pending is { } pending)
                {
                    if (!pending.IsCompleted) continue;
                    // A failed send is the connection's end, which the bot's life loop sees and recovers from.
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
                    // Blocked only by an ack answering an input on the current heading: acks still in flight from before
                    // a turn report the old heading's wall.
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
                    send = bot.SendInput(seq, dirX, dirZ, lane.Yaw, ct);
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

        /// <summary>Builds and fires the packet a fighter decided on besides its input, from the lane's reused messages.</summary>
        private void SendAction(Bot bot, InputLane lane, FighterStep step, CancellationToken ct)
        {
            ValueTask send;
            try
            {
                send = step.Action switch
                {
                    FighterAction.EnterForest => bot.SendInWorld(lane.EnterMap(Fighter.ForestMapId), NetworkPacketType.CMSG_ENTER_MAP, ct),
                    FighterAction.LeaveForest => bot.SendInWorld(lane.EnterMap(Fighter.TownMapId), NetworkPacketType.CMSG_ENTER_MAP, ct),
                    FighterAction.Cast => bot.SendInWorld(lane.Cast(bot.Fighter!.AbilityId, step.AimX, step.AimY, step.AimZ),
                        NetworkPacketType.CMSG_CAST_ABILITY, ct),
                    _ => bot.SendInWorld(InputLane.Respawn, NetworkPacketType.CMSG_RESPAWN_AT_TOWN, ct),
                };
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

/// <summary>
/// The input driver's step lateness over a window (<see cref="InputDriver.Lateness"/>), in milliseconds: the 95th
/// percentile over the steps of every loop together (<c>gen-lag</c>), and the highest of the loops' own 95th
/// percentiles; NaN with no step, or once a loop failed. <paramref name="Loops"/> is how many loops the driver runs.
/// </summary>
public readonly record struct DriverLateness(double P95Ms, double WorstLoopP95Ms, int Loops);

/// <summary>
/// How busy the input driver's loops were over a window (<see cref="InputDriver.Busy"/>): the mean time a loop spent per
/// step deciding and firing its bots' sends, as a share of a step, averaged over the loops and for the busiest one.
/// </summary>
public readonly record struct DriverBusy(double MeanShare, double BusiestShare);

/// <summary>
/// Which of the input driver's loops drives each bot: given once, when a loop first sees the bot in the world, and kept
/// for the bot's whole life (its reconnects and character changes included), so only that loop's thread ever touches the
/// bot's lane. Each bot goes to the loop holding the fewest bots of its behaviour, then the fewest bots, then the first:
/// every behaviour is shared out evenly over the loops, however the mix interleaves the bots, and so is the load.
/// </summary>
/// <remarks>
/// Not by bot index modulo the loop count: the mix gives behaviours by index in a short rotation (every ten indexes hold
/// 6 idle bots, 3 walkers and 1 churner by default), and a loop count sharing a factor with it would put a behaviour
/// on some loops only (with 5 loops, every churner on one). The counts are of the bots ever given: one that gave up
/// keeps its place, which a run's few such bots barely skew.
/// </remarks>
public sealed class LoopAssigner
{
    /// <summary>A bot no loop has been given yet.</summary>
    public const int Unassigned = -1;

    private readonly Lock _lock = new();
    private readonly int[,] _byBehaviour;
    private readonly int[] _total;

    public LoopAssigner(int loops)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(loops, 1);
        Loops = loops;
        _byBehaviour = new int[Enum.GetValues<BehaviourKind>().Length, loops];
        _total = new int[loops];
    }

    public int Loops { get; }

    /// <summary>
    /// The loop that drives the bot whose place is <paramref name="slot"/>: the one it was given, or, while it has none
    /// (<see cref="Unassigned"/>), one given now by <paramref name="behaviour"/>. Safe from any loop: the first call
    /// that finds the slot unassigned gives it, and every call returns that loop from then on.
    /// </summary>
    public int LoopOf(ref int slot, BehaviourKind behaviour)
    {
        int loop = Volatile.Read(ref slot);
        if (loop != Unassigned) return loop;

        lock (_lock)
        {
            loop = slot;
            if (loop != Unassigned) return loop;

            int kind = (int)behaviour;
            loop = 0;
            for (int candidate = 1; candidate < Loops; candidate++)
            {
                int fewer = _byBehaviour[kind, candidate].CompareTo(_byBehaviour[kind, loop]);
                if (fewer < 0 || (fewer == 0 && _total[candidate] < _total[loop])) loop = candidate;
            }

            _byBehaviour[kind, loop]++;
            _total[loop]++;
            Volatile.Write(ref slot, loop);
            return loop;
        }
    }
}

/// <summary>
/// What the input driver keeps of one bot between steps; touched only by the thread of the one driver loop that drives
/// the bot, but for <see cref="Loop"/>, which names that loop and which every loop reads.
/// </summary>
internal sealed class InputLane
{
    /// <summary>
    /// The driver loop that drives the bot, for its whole life; <see cref="LoopAssigner.Unassigned"/> until a loop first
    /// sees it in the world. Written once, by <see cref="LoopAssigner.LoopOf"/>.
    /// </summary>
    public int Loop = LoopAssigner.Unassigned;

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

    /// <summary>The fighter's entry message, reused: a message is serialized when its packet is built, on the loop's thread.</summary>
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
