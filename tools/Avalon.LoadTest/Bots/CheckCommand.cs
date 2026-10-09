using System.Diagnostics;
using System.Globalization;
using Avalon.LoadTest.Api;
using Avalon.LoadTest.Runs;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// <c>check</c>: one bot of a run end to end. It signs in, enters the run's world, sends idle input at 60 Hz for ten
/// seconds, leaves and signs out, printing each step's duration and the input-ack latency. Exit 0 when every step
/// passed, 1 with the failing step and its reason otherwise.
/// </summary>
public static class CheckCommand
{
    private const int InputsPerSecond = 60;

    private static readonly TimeSpan s_idleTime = TimeSpan.FromSeconds(10);

    /// <summary>How long the acks of the last inputs are waited for before the latency is read.</summary>
    private static readonly TimeSpan s_ackDrain = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan s_apiTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A leave after a failure or a cancel gets this long of its own.</summary>
    private static readonly TimeSpan s_cleanupTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(CheckOptions options, CancellationToken ct)
    {
        var run = RunFile.Load(options.RunId);
        if (options.Bot >= run.Bots.Count)
            throw new CommandLineException($"Run {run.RunId} has {run.Bots.Count} bots: --bot takes 0 to {run.Bots.Count - 1}.");

        string account = run.Bots[options.Bot];
        string dialling = options.Dial is null ? "" : $", dialling {options.Dial}";
        Console.WriteLine(Invariant($"Bot {options.Bot} ({account}) into world {run.WorldId} through {run.Api}{dialling}"));

        using var api = new ApiClient(run.Api, s_apiTimeout);
        var metrics = new BotMetrics();
        var bot = new Bot(options.Bot, account, run.BotPassword, api, run.WorldId, options.Dial, metrics)
        {
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
            await IdleAsync(bot, ct);
            StepClientValues values = metrics.TakeWindow();
            Console.WriteLine(Invariant(
                $"  ack        p50 {Ms(values.AckP50)}, p95 {Ms(values.AckP95)}, p99 {Ms(values.AckP99)} over {values.AckSamples} inputs"));
            if (values.AckSamples == 0)
                throw new BotStepException("idle", "idle:no-acks", "no input was answered while idle");

            left = true;
            await bot.LeaveAsync(ct);
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
    /// Idle input at 60 Hz for <see cref="s_idleTime"/>: each input is due on a fixed schedule from the start, slept
    /// towards while more than 2 ms away and yielded towards after, as the game client's fixed step sends.
    /// </summary>
    private static async Task IdleAsync(Bot bot, CancellationToken ct)
    {
        long start = Stopwatch.GetTimestamp();
        int inputs = (int)(s_idleTime.TotalSeconds * InputsPerSecond);
        for (int i = 0; i < inputs; i++)
        {
            long due = start + (long)(i * (double)Stopwatch.Frequency / InputsPerSecond);
            for (TimeSpan wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), due); wait > TimeSpan.Zero;
                 wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), due))
            {
                if (wait > TimeSpan.FromMilliseconds(2))
                    await Task.Delay(wait - TimeSpan.FromMilliseconds(2), ct);
                else
                    Thread.Yield();
            }

            if (bot.Closed.IsCompleted)
                throw new BotStepException("idle", "idle:closed", "the world closed the connection while the bot idled");

            try
            {
                await bot.SendAsync(bot.NextInput(bot.NextSeq(), 0f, 0f, 0), ct);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException)
            {
                throw new BotStepException("idle", "idle:io", error.Message);
            }
        }

        await Task.Delay(s_ackDrain, ct);
        bot.StepTimed?.Invoke("idle", Stopwatch.GetElapsedTime(start));
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
