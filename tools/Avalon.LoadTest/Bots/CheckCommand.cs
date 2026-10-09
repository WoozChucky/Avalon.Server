using System.Diagnostics;
using System.Globalization;
using Avalon.LoadTest.Api;
using Avalon.LoadTest.Runs;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// <c>check</c>: one bot of a run end to end. It signs in, enters the run's world, sends input at 60 Hz for ten seconds
/// through the <see cref="InputDriver"/> (idle, or walking with <c>--behaviour walker</c>), leaves and signs out,
/// printing each step's duration, the input-ack latency and the driver's lateness. Exit 0 when every step passed, the
/// leave and the sign-out included, 1 with the failing step and its reason otherwise.
/// </summary>
public static class CheckCommand
{
    private static readonly TimeSpan s_driveTime = TimeSpan.FromSeconds(10);

    /// <summary>How long the acks of the last inputs are waited for before the latency is read.</summary>
    private static readonly TimeSpan s_ackDrain = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan s_apiTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A leave after a failure or a cancel gets this long of its own.</summary>
    private static readonly TimeSpan s_cleanupTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(CheckOptions options, CancellationToken ct)
    {
        var run = RunFile.Load(options.RunId, forBots: true);
        if (options.Bot >= run.Bots.Count)
            throw new CommandLineException($"Run {run.RunId} has {run.Bots.Count} bots: --bot takes 0 to {run.Bots.Count - 1}.");

        string account = run.Bots[options.Bot];
        string dialling = options.Dial is null ? "" : $", dialling {options.Dial}";
        Console.WriteLine(Invariant($"Bot {options.Bot} ({account}) into world {run.WorldId} through {run.Api}{dialling}"));

        using var api = new ApiClient(run.Api, s_apiTimeout);
        var metrics = new BotMetrics();
        using var bot = new Bot(options.Bot, account, run.BotPassword, api, run.WorldId, options.Dial, metrics)
        {
            Behaviour = options.Behaviour,
            StepTimed = (step, took) => Console.WriteLine(Invariant($"  {step,-10} {took.TotalMilliseconds,8:0} ms")),
            Note = line => Console.Error.WriteLine("  " + line),
        };

        bool left = false;
        try
        {
            await bot.SignInAsync(ct);
            await bot.EnterAsync(takeover: false, ct);

            // The entry's idle probes are not the latency asked about.
            metrics.TakeWindow();
            string step = options.Behaviour == BehaviourKind.Walker ? "walk" : "idle";
            double latenessP95 = await DriveAsync(bot, step, ct);
            StepClientValues values = metrics.TakeWindow();
            Console.WriteLine(Invariant(
                $"  ack        p50 {Ms(values.AckP50)}, p95 {Ms(values.AckP95)}, p99 {Ms(values.AckP99)} over {values.AckSamples} inputs"));
            Console.WriteLine(Invariant($"  driver     lateness p95 {Ms(latenessP95)}"));
            if (values.AckSamples == 0)
                throw new BotStepException(step, $"{step}:no-acks", "no input was answered");

            left = true;
            await bot.LeaveAsync(ct);
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
                Console.Error.WriteLine("Check failed at logout: the game context's sign-out failed; it expires within 5 minutes");
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
            if (!left) await LeaveQuietlyAsync(bot);
        }
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
