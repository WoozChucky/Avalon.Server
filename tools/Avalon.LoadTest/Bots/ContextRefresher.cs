using System.Runtime.CompilerServices;
using Avalon.LoadTest.Api;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// Keeps every bot's game context alive: a context not refreshed expires after 5 minutes, and the bot's world session
/// with it. Every 5 seconds the bots due are refreshed, a bot being due from its <see cref="GameContext.RenewAt"/> less
/// a jitter of 0 to 20 seconds drawn once per bot, so contexts signed in together do not all refresh in the same pass.
/// A failed refresh is counted as a sign-in failure (<c>refresh</c>) and tried again the next pass.
/// </summary>
public sealed class ContextRefresher(ApiClient api, Func<IReadOnlyCollection<Bot>> bots, BotMetrics metrics)
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
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task RefreshAsync(Bot bot, GameContext context, CancellationToken ct)
    {
        try
        {
            await api.RefreshAsync(context, ct);
        }
        catch (ApiException error)
        {
            // A context the bot let go of since (a leave signs it out) is not a failure.
            if (!ReferenceEquals(bot.Context, context) || bot.State is BotState.Leaving or BotState.Stopped) return;

            metrics.SignInFailed("refresh");
            bot.Note?.Invoke($"Refreshing the game context failed: {error.Message}; trying again in {s_pass.TotalSeconds:0} s.");
        }
    }

    private TimeSpan Jitter(Bot bot) =>
        _jitter.GetValue(bot, static _ => new StrongBox<TimeSpan>(s_maxJitter * Random.Shared.NextDouble())).Value;
}
