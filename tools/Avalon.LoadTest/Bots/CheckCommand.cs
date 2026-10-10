using System.Diagnostics;
using System.Globalization;
using Avalon.LoadTest.Api;
using Avalon.LoadTest.Runs;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// <c>check</c>: one bot of a run end to end. It signs in, enters the run's world, sends input at 60 Hz for ten seconds
/// through the <see cref="InputDriver"/> (idle, or walking with <c>--behaviour walker</c>), leaves and signs out,
/// printing each step's duration, the input-ack latency and the driver's lateness. With <c>--behaviour fighter</c> it
/// drives forest trips instead, at once (no first-trip wait), printing each stage (to the portal, portal request to
/// transition, the fight, to the exit, the exit, or the respawn), the casts sent and refused, the kills, the deaths and
/// any failure. A trip that ended in a death is made again, up to <see cref="CheckTrips.MostTrips"/> trips
/// (<see cref="CheckTrips"/>). With <c>--party-size N</c> as well, N bots from <c>--bot</c> on do it together: they
/// form one party (<see cref="PartyFormer"/>) once all are in, set out at once and share one forest. Exit 0 when every
/// step passed (for a fighter, a trip completed: it walked out into town; for a party, it formed and every member
/// completed a trip), the leaves and the sign-outs included, 1 with the failing step and its reason otherwise.
/// </summary>
public static class CheckCommand
{
    private static readonly TimeSpan s_driveTime = TimeSpan.FromSeconds(10);

    /// <summary>How long the acks of the last inputs are waited for before the latency is read.</summary>
    private static readonly TimeSpan s_ackDrain = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan s_apiTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A leave after a failure or a cancel gets this long of its own.</summary>
    private static readonly TimeSpan s_cleanupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A fighter's trip ends within <c>--forest-time</c>, the most its exit budget can be
    /// (<see cref="Fighter.MostExitBudget"/>) and this: the walk to the portal (20 s), the entry (30 s), a respawn
    /// (30 s), a hold for a fresh ack (10 s), and a margin.
    /// </summary>
    private static readonly TimeSpan s_tripMargin = TimeSpan.FromMinutes(3);

    public static async Task<int> RunAsync(CheckOptions options, CancellationToken ct)
    {
        var run = RunFile.Load(options.RunId, forBots: true);
        int size = options.PartySize;
        if (options.Bot + size > run.Bots.Count)
        {
            throw new CommandLineException(size == 1
                ? $"Run {run.RunId} has {run.Bots.Count} bots: --bot takes 0 to {run.Bots.Count - 1}."
                : $"Run {run.RunId} has {run.Bots.Count} bots: a party of {size} from --bot takes 0 to {run.Bots.Count - size}.");
        }

        string dialling = options.Dial is null ? "" : $", dialling {options.Dial}";
        string who = size == 1
            ? $"Bot {options.Bot} ({run.Bots[options.Bot]})"
            : $"Bots {options.Bot} to {options.Bot + size - 1} ({string.Join(", ", run.Bots.Skip(options.Bot).Take(size))}), one party,";
        Console.WriteLine(Invariant($"{who} into world {run.WorldId} through {run.Api}{dialling}"));

        using var api = new ApiClient(run.Api, s_apiTimeout);
        var metrics = new BotMetrics();
        Action<string> printNote = line => Console.Error.WriteLine("  " + line);
        var bots = new Bot[size];
        for (int i = 0; i < size; i++)
        {
            int index = options.Bot + i;
            // A party's lines name the member they are about.
            string member = size == 1 ? "" : Invariant($"[{index}] ");
            Action<string, TimeSpan> printStep = (step, took) =>
                Console.WriteLine(Invariant($"  {member}{step,-10} {took.TotalMilliseconds,8:0} ms"));
            Fighter? fighter = options.Behaviour == BehaviourKind.Fighter
                ? new Fighter(index, metrics, options.ForestTime, firstTripJitter: TimeSpan.Zero)
                {
                    StepTimed = printStep,
                    Note = line => printNote(member + line),
                }
                : null;
            bots[i] = new Bot(index, run.Bots[index], run.BotPassword, api, run.WorldId, options.Dial, metrics)
            {
                Behaviour = options.Behaviour,
                Fighter = fighter,
                StepTimed = printStep,
                Note = line => printNote(member + line),
            };
        }

        // A party sets out as soon as it has formed.
        BotParty? party = size == 1 ? null : new BotParty(bots, metrics, departJitter: TimeSpan.Zero) { Note = printNote };
        for (int i = 0; party is not null && i < size; i++)
        {
            bots[i].Party = party.Links[i];
            bots[i].Fighter!.ReadyToLeaveTown = party.Links[i].ReadyToLeave;
        }

        bool left = false;
        try
        {
            foreach (Bot bot in bots) await bot.SignInAsync(ct);
            await Task.WhenAll(bots.Select(bot => bot.EnterAsync(takeover: false, ct)));
            if (party is not null) await FormAsync(party, metrics, ct);

            // The entry's idle probes are not the latency asked about.
            metrics.TakeWindow();
            string step = options.Behaviour switch
            {
                BehaviourKind.Walker => "walk",
                BehaviourKind.Fighter => "forest",
                _ => "idle",
            };
            (double latenessP95, CheckTripsVerdict trips) = options.Behaviour == BehaviourKind.Fighter
                ? await TripAsync(bots, options.ForestTime, ct)
                : (await DriveAsync(bots[0], step, ct), CheckTripsVerdict.Passed);
            StepClientValues values = metrics.TakeWindow();
            Console.WriteLine(Invariant(
                $"  ack        p50 {Ms(values.AckP50)}, p95 {Ms(values.AckP95)}, p99 {Ms(values.AckP99)} over {values.AckSamples} inputs"));
            Console.WriteLine(Invariant($"  driver     lateness p95 {Ms(latenessP95)}"));
            Console.WriteLine($"  encryption {BotMetrics.EncryptionText(metrics.Admissions)}");
            if (options.Behaviour == BehaviourKind.Fighter) PrintTrip(values);
            if (values.AckSamples == 0)
                throw new BotStepException(step, $"{step}:no-acks", "no input was answered");

            if (trips == CheckTripsVerdict.Failed || values.PartyFormFailures.Count > 0)
            {
                IEnumerable<string> kinds = values.FighterFailures.Keys.Concat(values.PartyFormFailures.Keys);
                throw new BotStepException(step, "forest:failed",
                    $"a step of the trip failed: {string.Join(", ", kinds.Order(StringComparer.Ordinal))}");
            }

            if (trips == CheckTripsVerdict.DiedThrice)
            {
                throw new BotStepException(step, "forest:died-thrice",
                    Invariant($"a fighter died on each of its {CheckTrips.MostTrips} trips (it respawned in town each time)"));
            }

            left = true;
            foreach (Bot bot in bots) await bot.LeaveAsync(ct);
            // The leave and the sign-out note their failures rather than throw: a check that could not leave cleanly
            // did not pass.
            StepClientValues leaving = metrics.TakeWindow();
            if (leaving.LeaveFailures.Count > 0)
            {
                Console.Error.WriteLine($"Check failed at leave: {string.Join(", ", leaving.LeaveFailures.Keys.Order(StringComparer.Ordinal))}");
                return 1;
            }

            if (leaving.SignOutFailures > 0)
            {
                Console.Error.WriteLine("Check failed at logout: a game context's sign-out failed; it expires within 5 minutes");
                return 1;
            }

            Console.WriteLine("Check passed.");
            return 0;
        }
        catch (BotStepException error)
        {
            Console.Error.WriteLine($"Check failed at {error.Step}: {error.Reason}");
            return 1;
        }
        finally
        {
            if (!left)
            {
                foreach (Bot bot in bots) await LeaveQuietlyAsync(bot);
            }
        }
    }

    /// <summary>
    /// Forms the check's party, printing how long it took; a party that did not form (twice) fails the check with the
    /// reason counted.
    /// </summary>
    private static async Task FormAsync(BotParty party, BotMetrics metrics, CancellationToken ct)
    {
        long start = Stopwatch.GetTimestamp();
        if (!await PartyFormer.FormAsync(party, ct))
        {
            StepClientValues values = metrics.TakeWindow();
            throw new BotStepException("party", "party:failed",
                $"the party did not form: {string.Join(", ", values.PartyFormFailures.Keys.Order(StringComparer.Ordinal))}");
        }

        Console.WriteLine(Invariant($"  party      {Stopwatch.GetElapsedTime(start).TotalMilliseconds,8:0} ms ({party.Members.Count} members)"));
    }

    /// <summary>
    /// Runs the input driver over this one bot for <see cref="s_driveTime"/>, then waits for the last acks. Returns the
    /// driver's lateness p95; a walker also prints how far it got from where it started.
    /// </summary>
    private static async Task<double> DriveAsync(Bot bot, string step, CancellationToken ct)
    {
        Bot[] inWorld = [bot];
        var driver = new InputDriver(() => inWorld);
        BotAck from = bot.LastAck;
        long start = Stopwatch.GetTimestamp();

        using (var stop = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            stop.CancelAfter(s_driveTime);
            Task driving = driver.RunAsync(stop.Token);
            Task closed = bot.Closed;
            if (await Task.WhenAny(driving, closed) == closed)
            {
                await stop.CancelAsync();
                await driving;
                throw new BotStepException(step, $"{step}:closed", "the world closed the connection while the bot was driven");
            }

            await driving;
        }

        ct.ThrowIfCancellationRequested();
        await Task.Delay(s_ackDrain, ct);
        bot.StepTimed?.Invoke(step, Stopwatch.GetElapsedTime(start));
        if (bot.Behaviour == BehaviourKind.Walker)
        {
            BotAck to = bot.LastAck;
            double moved = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Z - from.Z) * (to.Z - from.Z));
            Console.WriteLine(Invariant($"  walked     {moved:0.0} m from the spawn point (straight line)"));
        }

        return driver.LatenessP95Ms();
    }

    /// <summary>
    /// Runs the input driver over the fighters until each one is done (<see cref="CheckTrips"/>: a trip completed or
    /// failed, or it died on each of its trips), or until one is done without a completed trip, which decides the
    /// verdict; it prints each death, then waits for the last acks. Returns the
    /// driver's lateness p95 and the trips' verdict. A connection the world closes, or a member not done within
    /// <see cref="CheckTrips.MostTrips"/> times <c>--forest-time</c>, the most its exit budget can be and
    /// <see cref="s_tripMargin"/>, fails the check.
    /// </summary>
    private static async Task<(double LatenessP95, CheckTripsVerdict Trips)> TripAsync(Bot[] bots, TimeSpan forestTime,
        CancellationToken ct)
    {
        const string Step = "forest";
        var trips = new CheckTrips(bots.Length);
        for (int i = 0; i < bots.Length; i++)
        {
            int member = i;
            Bot bot = bots[i];
            Fighter fighter = bot.Fighter!;
            fighter.TripEnded += end =>
            {
                if (trips.Ended(member, end))
                {
                    bot.Note?.Invoke(Invariant(
                        $"Died on trip {trips.Trips(member)} of {CheckTrips.MostTrips}; it respawned in town and goes again."));
                }
            };
            // A member done stands in town while the others go on.
            Func<long, bool>? party = fighter.ReadyToLeaveTown;
            fighter.ReadyToLeaveTown = now => trips.MaySetOut(member) && (party is null || party(now));
        }

        var driver = new InputDriver(() => bots);
        TimeSpan limit = CheckTrips.MostTrips * (forestTime + Fighter.MostExitBudget(forestTime) + s_tripMargin);
        long start = Stopwatch.GetTimestamp();

        Task all = trips.AllDone;
        using (var stop = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            Task driving = driver.RunAsync(stop.Token);
            Task closed = Task.WhenAny(bots.Select(bot => bot.Closed));
            Task first = await Task.WhenAny(all, closed, Task.Delay(limit, stop.Token));
            await stop.CancelAsync();
            await driving;
            ct.ThrowIfCancellationRequested();
            if (!all.IsCompleted)
            {
                throw first == closed
                    ? new BotStepException(Step, $"{Step}:closed", "the world closed a connection during the trip")
                    : new BotStepException(Step, $"{Step}:timeout",
                        Invariant($"a fighter's trips had not ended {limit.TotalSeconds:0} s after the first began"));
            }
        }

        await Task.Delay(s_ackDrain, ct);
        if (bots.Length == 1) bots[0].StepTimed?.Invoke(Step, Stopwatch.GetElapsedTime(start));
        else Console.WriteLine(Invariant($"  {Step,-10} {Stopwatch.GetElapsedTime(start).TotalMilliseconds,8:0} ms (every member's trips)"));
        return (driver.LatenessP95Ms(), trips.Verdict);
    }

    /// <summary>What a fighter's trip counted.</summary>
    private static void PrintTrip(StepClientValues values)
    {
        Console.WriteLine(Invariant(
            $"  enter      {values.ForestEntries} forest entries, portal request to transition p50 {Ms(values.ForestEntryP50)}"));
        string refused = values.CastsRefused.Count == 0
            ? "none refused"
            : $"refused {string.Join(", ", values.CastsRefused.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => Invariant($"{pair.Key} {pair.Value}")))}";
        Console.WriteLine(Invariant($"  casts      {values.CastsSent} sent, {refused}"));
        Console.WriteLine(Invariant($"  kills      {values.Kills} seen, {values.OwnDeaths} own deaths"));
        Console.WriteLine(Invariant($"  exit       {values.ForestTrips} trips completed"));
        foreach ((string kind, int count) in values.FighterFailures.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine(Invariant($"  failed     {kind} x{count}"));
    }

    /// <summary>Leaves and signs out after a failure or a cancel, so the account's session does not linger; best effort.</summary>
    private static async Task LeaveQuietlyAsync(Bot bot)
    {
        using var limit = new CancellationTokenSource(s_cleanupTimeout);
        try
        {
            await bot.LeaveAsync(limit.Token);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"  Leaving after the failure did not finish: {error.Message}");
        }
    }

    private static string Ms(double value) => double.IsFinite(value) ? Invariant($"{value:0.0} ms") : "n/a";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
