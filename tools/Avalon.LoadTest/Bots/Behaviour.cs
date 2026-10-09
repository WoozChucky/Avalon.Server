using System.Globalization;

namespace Avalon.LoadTest.Bots;

/// <summary>What a bot does once in the world.</summary>
public enum BehaviourKind
{
    /// <summary>Stands still: a zero-direction input every step.</summary>
    Idle,

    /// <summary>Walks a heading every step and turns when a wall stops it.</summary>
    Walker,

    /// <summary>Walks as a walker; every 2 to 5 minutes changes character, every fourth time reconnecting instead.</summary>
    Churner,
}

/// <summary>The share of each behaviour in a run (<c>--mix idle=60,walker=30,churner=10</c>).</summary>
public static class Mix
{
    /// <summary>The spec's default mix.</summary>
    public const string Default = "idle=60,walker=30,churner=10";

    /// <summary>
    /// <c>name=weight</c> pairs separated by commas, each behaviour at most once, weights whole numbers of 0 or more with
    /// at least one above 0. A behaviour left out has weight 0. Refused with a <see cref="CommandLineException"/>.
    /// </summary>
    public static IReadOnlyList<(BehaviourKind Kind, int Weight)> Parse(string text)
    {
        var mix = new List<(BehaviourKind Kind, int Weight)>();
        foreach (string part in text.Split(',', StringSplitOptions.TrimEntries))
        {
            string[] pair = part.Split('=', StringSplitOptions.TrimEntries);
            // Letters only: Enum.TryParse would also take a number.
            if (pair.Length != 2 || pair[0].Length == 0 || !pair[0].All(char.IsAsciiLetter) ||
                !Enum.TryParse(pair[0], ignoreCase: true, out BehaviourKind kind))
            {
                throw new CommandLineException($"--mix takes name=weight pairs of idle, walker and churner, not \"{part}\".");
            }

            if (!int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out int weight) || weight > 1000)
                throw new CommandLineException($"--mix: {pair[0]}'s weight is a whole number, 0 to 1000.");

            if (mix.Exists(entry => entry.Kind == kind))
                throw new CommandLineException($"--mix names {pair[0]} twice.");

            mix.Add((kind, weight));
        }

        if (!mix.Exists(entry => entry.Weight > 0))
            throw new CommandLineException("--mix needs at least one behaviour with a weight above 0.");

        return mix;
    }

    /// <summary>
    /// The behaviour of the bot at <paramref name="index"/>: smooth weighted round-robin over the mix, so every prefix of
    /// the bots (each ramp step's count) holds the shares as closely as whole bots can, the behaviours interleaved.
    /// </summary>
    public static BehaviourKind For(int index, IReadOnlyList<(BehaviourKind Kind, int Weight)> mix)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int total = 0;
        foreach ((BehaviourKind _, int weight) in mix) total += weight;
        if (total <= 0) throw new ArgumentException("The mix has no weight.", nameof(mix));

        // The rotation repeats every `total` picks; the index-th pick of the first period is the one.
        Span<int> current = mix.Count <= 16 ? stackalloc int[mix.Count] : new int[mix.Count];
        int pick = 0;
        for (int step = 0; step <= index % total; step++)
        {
            pick = 0;
            for (int i = 0; i < mix.Count; i++)
            {
                current[i] += mix[i].Weight;
                if (current[i] > current[pick]) pick = i;
            }

            current[pick] -= total;
        }

        return mix[pick].Kind;
    }
}

/// <summary>
/// A walker's heading: kept step after step; when the last step was blocked (a wall), a new one is picked, turned
/// 90 to 270 degrees from the old so the walker leaves the wall. The first call picks a random heading. One walker per
/// bot, used by the input driver's thread only.
/// </summary>
public sealed class Walker
{
    private bool _started;
    private float _dirX;
    private float _dirZ;
    private float _yaw;

    /// <summary>The direction to send this step (unit length) and the yaw in degrees that faces it (0 = +Z, 90 = +X).</summary>
    public (float DirX, float DirZ, ushort Yaw) Next(bool blockedLastStep, Random rng)
    {
        if (!_started || blockedLastStep)
        {
            _yaw = _started
                ? (_yaw + 90f + (float)rng.NextDouble() * 180f) % 360f
                : (float)rng.NextDouble() * 360f;
            _started = true;
            float radians = _yaw * MathF.PI / 180f;
            _dirX = MathF.Sin(radians);
            _dirZ = MathF.Cos(radians);
        }

        return (_dirX, _dirZ, (ushort)((int)_yaw % 360));
    }
}

/// <summary>
/// What keeps a bot in the world after its first entry, besides the input driver: a churner's character changes and
/// reconnects, and a reconnect after the connection closed unasked. One loop per bot entered by the runner.
/// </summary>
public static class BotLife
{
    /// <summary>A churner stays in the world this long, at least, between churns.</summary>
    private static readonly TimeSpan s_churnMin = TimeSpan.FromMinutes(2);

    /// <summary>... and at most this long.</summary>
    private static readonly TimeSpan s_churnMax = TimeSpan.FromMinutes(5);

    /// <summary>After an entry failed with every retry spent, the bot tries again after this long.</summary>
    private static readonly TimeSpan s_reentryPause = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs until <paramref name="ct"/> is cancelled. A churner, in the world, waits a random 2 to 5 minutes, then
    /// changes character on its connection; every fourth churn it reconnects instead (closes, keeping its game context,
    /// and enters again with a takeover). A connection that closes while the bot is in the world and nothing asked for
    /// it is counted as a disconnect, and the bot enters again with a takeover. A churn that fails, or an entry that
    /// fails all its attempts, is counted by the bot and retried. Cancel <paramref name="ct"/> and wait for this task
    /// before leaving the bot: a leave seen from here would pass for a disconnect.
    /// </summary>
    public static async Task RunAsync(Bot bot, BotMetrics metrics, CancellationToken ct)
    {
        int churns = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (bot.State != BotState.InWorld)
                {
                    if (!await ReenterAsync(bot, ct)) return;
                    continue;
                }

                Task closed = bot.Closed;
                bool churnDue = bot.Behaviour == BehaviourKind.Churner
                    ? await ChurnDueAsync(closed, ct)
                    : await WaitClosedAsync(closed, ct);
                if (ct.IsCancellationRequested) return;

                if (!churnDue)
                {
                    // Only this loop closes a connection while the bot is in the world, and it is not closing one.
                    metrics.Disconnected(bot.Index);
                    bot.Note?.Invoke("The connection closed while in the world; entering again.");
                    await bot.DisconnectAsync(ct);
                    continue;
                }

                if (bot.State != BotState.InWorld) continue;

                churns++;
                try
                {
                    if (churns % 4 == 0)
                    {
                        // A full reconnect keeps the game context: LeaveAsync would sign it out.
                        await bot.DisconnectAsync(ct);
                        await bot.EnterAsync(takeover: true, ct);
                    }
                    else
                    {
                        await bot.ChangeCharacterAsync(ct);
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Counted by the bot; the connection may be in any state, so it is closed and entered afresh.
                    bot.Note?.Invoke($"Churn failed: {Reason(error)}.");
                    await bot.DisconnectAsync(ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>True when the churn is due, false when the connection closed first.</summary>
    private static async Task<bool> ChurnDueAsync(Task closed, CancellationToken ct)
    {
        TimeSpan stay = s_churnMin + (s_churnMax - s_churnMin) * Random.Shared.NextDouble();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var due = Task.Delay(stay, wait.Token);
        Task first = await Task.WhenAny(closed, due);
        // The delay is not left running for minutes after the connection closed.
        await wait.CancelAsync();
        return first == due && !closed.IsCompleted;
    }

    /// <summary>Waits for the connection to end; false (no churn) unless cancelled.</summary>
    private static async Task<bool> WaitClosedAsync(Task closed, CancellationToken ct)
    {
        // A faulted read loop is a close like any other; its exception is not this loop's.
        await closed.WaitAsync(ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return false;
    }

    /// <summary>
    /// Enters again with a takeover, the account's old session possibly still live; after a failure that spent every
    /// attempt, waits and tries again. False when the bot cannot enter at all (signed out).
    /// </summary>
    private static async Task<bool> ReenterAsync(Bot bot, CancellationToken ct)
    {
        if (bot.Context is null || bot.State is BotState.Leaving or BotState.Stopped or BotState.SignedOut) return false;

        try
        {
            await bot.EnterAsync(takeover: true, ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Counted by the bot, which also closed what the attempt opened.
            bot.Note?.Invoke($"Entering again failed: {Reason(error)}; next try in {s_reentryPause.TotalSeconds:0} s.");
            await Task.Delay(s_reentryPause, ct);
        }

        return true;
    }

    private static string Reason(Exception error) =>
        error is BotStepException step ? $"{step.Step}: {step.Reason}" : error.Message;
}
