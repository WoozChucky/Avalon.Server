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

    /// <summary>Walks from town into a forest of its own, fights its creatures, walks out and repeats (<see cref="Bots.Fighter"/>).</summary>
    Fighter,
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
                throw new CommandLineException($"--mix takes name=weight pairs of idle, walker, churner and fighter, not \"{part}\".");
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
            _yaw = _started ? Heading.TurnAway(_yaw, rng) : (float)rng.NextDouble() * 360f;
            _started = true;
            (_dirX, _dirZ) = Heading.Direction(_yaw);
        }

        return (_dirX, _dirZ, Heading.Wire(_yaw));
    }
}

/// <summary>
/// Headings on the ground plane as the input carries them: a yaw in degrees, 0 facing +Z and 90 facing +X, and the
/// unit direction it faces. Shared by walkers and fighters, which both turn away from a wall the same way.
/// </summary>
public static class Heading
{
    /// <summary>A new heading after a wall stopped <paramref name="yaw"/>: turned 90 to 270 degrees, so it leaves the wall.</summary>
    public static float TurnAway(float yaw, Random rng) => (yaw + 90f + (float)rng.NextDouble() * 180f) % 360f;

    /// <summary>The unit direction <paramref name="yaw"/> faces.</summary>
    public static (float DirX, float DirZ) Direction(float yaw)
    {
        float radians = yaw * MathF.PI / 180f;
        return (MathF.Sin(radians), MathF.Cos(radians));
    }

    /// <summary>The yaw, 0 to 360, that faces along (<paramref name="dx"/>, <paramref name="dz"/>).</summary>
    public static float Toward(float dx, float dz)
    {
        float yaw = MathF.Atan2(dx, dz) * 180f / MathF.PI;
        return yaw < 0f ? yaw + 360f : yaw;
    }

    /// <summary>The yaw as the input carries it: whole degrees, 0 to 359.</summary>
    public static ushort Wire(float yaw) => (ushort)((int)yaw % 360);
}

/// <summary>
/// What keeps a bot in the world after its first entry, besides the input driver: a churner's character changes and
/// reconnects, a fighter's reconnect when it asks for one, and a reconnect after the connection closed unasked. One loop
/// per bot entered by the runner.
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
    /// and enters again with a takeover). A fighter reconnects the same way when it asks to
    /// (<see cref="Fighter.ReconnectAsked"/>: it could not find its way out of the forest). A connection that closes
    /// while the bot is in the world and nothing asked for it is counted as a disconnect, and the bot enters again with
    /// a takeover. A churn or a fighter's reconnect that fails, or an entry that fails all its attempts, is counted by
    /// the bot and retried after a pause. When the bot gives up for good
    /// (<see cref="Bot.GaveUp"/>, its context lost), its connection is closed and the loop ends. Cancel
    /// <paramref name="ct"/> and wait for this task before leaving the bot: a leave seen from here would pass for a
    /// disconnect.
    /// </summary>
    /// <param name="pauseFirst">
    /// The bot's first entry failed every attempt: it waits the same pause before entering again, as after any entry
    /// that spent its retries, rather than adding a burst of attempts to a step that is already refusing them.
    /// </param>
    public static async Task RunAsync(Bot bot, BotMetrics metrics, CancellationToken ct, bool pauseFirst = false)
    {
        using var life = CancellationTokenSource.CreateLinkedTokenSource(ct, bot.GaveUp);
        try
        {
            if (pauseFirst) await Task.Delay(s_reentryPause, life.Token);
            await LiveAsync(bot, metrics, life.Token);
        }
        catch (OperationCanceledException) when (life.IsCancellationRequested)
        {
        }

        if (bot.GaveUp.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            try
            {
                await bot.DisconnectAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task LiveAsync(Bot bot, BotMetrics metrics, CancellationToken ct)
    {
        int churns = 0;
        while (!ct.IsCancellationRequested)
        {
            if (bot.State != BotState.InWorld)
            {
                if (!await ReenterAsync(bot, ct)) return;
                continue;
            }

            Task closed = bot.Closed;
            Wake wake = bot.Fighter is { } fighter
                ? await ReconnectAskedAsync(closed, fighter, ct)
                : bot.Behaviour == BehaviourKind.Churner
                    ? await ChurnDueAsync(closed, ct)
                    : await WaitClosedAsync(closed, ct);
            if (ct.IsCancellationRequested) return;

            // Whatever woke the loop, the bot is about to enter again: a fighter's ask made on the old connection is
            // answered by it, and must not wake the loop again once the bot is back.
            bot.Fighter?.TakeReconnect();

            if (wake == Wake.Closed)
            {
                // Only this loop closes a connection while the bot is in the world, and it is not closing one.
                metrics.Disconnected(bot.Index);
                bot.Note?.Invoke("The connection closed while in the world; entering again.");
                await bot.DisconnectAsync(ct);
                continue;
            }

            if (bot.State != BotState.InWorld) continue;

            // A fighter that asked (its way out of the forest timed out) reconnects; a churner reconnects every fourth
            // churn and changes character otherwise.
            bool reconnect = true;
            if (wake == Wake.Churn)
            {
                churns++;
                reconnect = churns % 4 == 0;
            }

            try
            {
                if (reconnect)
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
                bot.Note?.Invoke($"{(wake == Wake.Reconnect ? "Reconnecting" : "Churn")} failed: {Reason(error)}.");
                await bot.DisconnectAsync(ct);
                // A reconnect's entry already spent its own retries: wait before entering again.
                if (reconnect) await Task.Delay(s_reentryPause, ct);
            }
        }
    }

    /// <summary>The churn when it is due, <see cref="Wake.Closed"/> when the connection closed first.</summary>
    private static async Task<Wake> ChurnDueAsync(Task closed, CancellationToken ct)
    {
        TimeSpan stay = s_churnMin + (s_churnMax - s_churnMin) * Random.Shared.NextDouble();
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var due = Task.Delay(stay, wait.Token);
        Task first = await Task.WhenAny(closed, due);
        // The delay is not left running for minutes after the connection closed.
        await wait.CancelAsync();
        return first == due && !closed.IsCompleted ? Wake.Churn : Wake.Closed;
    }

    /// <summary>
    /// <see cref="Wake.Reconnect"/> when the fighter asks for a reconnect, <see cref="Wake.Closed"/> when the connection
    /// closed first.
    /// </summary>
    private static async Task<Wake> ReconnectAskedAsync(Task closed, Fighter fighter, CancellationToken ct)
    {
        Task asked = fighter.ReconnectAsked;
        Task first = await Task.WhenAny(closed, asked).WaitAsync(ct);
        return first == asked && !closed.IsCompleted ? Wake.Reconnect : Wake.Closed;
    }

    /// <summary>Waits for the connection to end: <see cref="Wake.Closed"/>, unless cancelled.</summary>
    private static async Task<Wake> WaitClosedAsync(Task closed, CancellationToken ct)
    {
        // A faulted read loop is a close like any other; its exception is not this loop's.
        await closed.WaitAsync(ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return Wake.Closed;
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

    /// <summary>What ended a wait of the loop in the world.</summary>
    private enum Wake
    {
        /// <summary>The connection closed without being asked to.</summary>
        Closed,

        /// <summary>A churner's churn is due.</summary>
        Churn,

        /// <summary>A fighter asked for a reconnect.</summary>
        Reconnect,
    }
}
