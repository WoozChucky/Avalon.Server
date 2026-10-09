using System.Runtime.CompilerServices;
using Avalon.LoadTest.Api;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// Keeps every bot's game context alive: a context not refreshed expires after 5 minutes, and the bot's world session
/// with it. Every 5 seconds the bots due are refreshed, a bot being due from its <see cref="GameContext.RenewAt"/> less
/// a jitter of 0 to 20 seconds drawn once per bot, so contexts signed in together do not all refresh in the same pass.
/// A failed refresh is counted as a sign-in failure (<c>refresh</c>) and tried again the next pass, under the same
/// <c>Idempotency-Key</c> while the refresh token is the same (<see cref="GameContextTokens.RefreshKey"/>). A context
/// that can no longer be refreshed (401: revoked, its token spent or unknown) is counted once
/// (<c>refresh:&lt;code&gt;</c>) and replaced by a fresh sign-in (<see cref="Bot.SignInAgainAsync"/>), taking a slot of
/// <paramref name="signIns"/>, the runner's bound on concurrent sign-ins; a bot whose sign-in fails gives up for good.
/// </summary>
public sealed class ContextRefresher(ApiClient api, Func<IReadOnlyCollection<Bot>> bots, BotMetrics metrics,
    SemaphoreSlim signIns)
{
    private static readonly TimeSpan s_pass = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_maxJitter = TimeSpan.FromSeconds(20);

    /// <summary>How many refreshes run at once; a pass holds them all before the next.</summary>
    private const int Concurrency = 16;

    private readonly ConditionalWeakTable<Bot, StrongBox<TimeSpan>> _jitter = [];

    /// <summary>Refreshes the bots due every 5 seconds until <paramref name="ct"/> is cancelled; the task then completes.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(s_pass);
        try
        {
            do
            {
                await PassAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task PassAsync(CancellationToken ct)
    {
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var due = new List<(Bot Bot, GameContext Context)>();
            foreach (Bot bot in bots())
            {
                // A bot leaving signs its context out; one stopped has none.
                if (bot.State is BotState.Leaving or BotState.Stopped || bot.Context is not { } context) continue;
                if (context.RenewAt - Jitter(bot) <= now) due.Add((bot, context));
            }

            await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = ct },
                (entry, token) => new ValueTask(RefreshAsync(entry.Bot, entry.Context, token)));
        }
        catch (Exception error) when (!(error is OperationCanceledException && ct.IsCancellationRequested))
        {
            // Whatever went wrong with this pass, the next one runs.
            metrics.SignInFailed("refresh:unexpected");
        }
    }

    private async Task RefreshAsync(Bot bot, GameContext context, CancellationToken ct)
    {
        try
        {
            await api.RefreshAsync(context, ct);
        }
        catch (Exception error) when (!(error is OperationCanceledException && ct.IsCancellationRequested))
        {
            // A context the bot let go of since (a leave signs it out) is not a failure.
            if (!ReferenceEquals(bot.Context, context) || bot.State is BotState.Leaving or BotState.Stopped) return;

            if (error is ApiException { Status: 401 } refused)
            {
                metrics.SignInFailed($"refresh:{refused.Detail}");
                bot.Note?.Invoke($"The game context cannot be refreshed ({refused.Detail}); signing in again.");
                await SignInAgainAsync(bot, ct);
                return;
            }

            metrics.SignInFailed(error is ApiException ? "refresh" : "refresh:unexpected");
            bot.Note?.Invoke($"Refreshing the game context failed: {error.Message}; trying again in {s_pass.TotalSeconds:0} s.");
        }
    }

    private async Task SignInAgainAsync(Bot bot, CancellationToken ct)
    {
        await signIns.WaitAsync(ct);
        try
        {
            await bot.SignInAgainAsync(ct);
        }
        finally
        {
            signIns.Release();
        }
    }

    private TimeSpan Jitter(Bot bot) =>
        _jitter.GetValue(bot, static _ => new StrongBox<TimeSpan>(s_maxJitter * Random.Shared.NextDouble())).Value;
}
