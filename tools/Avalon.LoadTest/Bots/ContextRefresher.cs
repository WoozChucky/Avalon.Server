using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Avalon.LoadTest.Api;

namespace Avalon.LoadTest.Bots;

/// <summary>
/// Keeps every bot's game context alive: a context not refreshed expires after 5 minutes, and the bot's world session
/// with it. Every 5 seconds the bots due are refreshed, a bot being due from its <see cref="GameContext.RenewAt"/> less
/// a jitter of 0 to 20 seconds drawn once per bot, so contexts signed in together do not all refresh in the same pass.
/// A failed refresh is counted as a sign-in failure (<c>refresh</c>) and tried again the next pass, under the same
/// <c>Idempotency-Key</c> while the refresh token is the same (<see cref="GameContextTokens.RefreshKey"/>). A context
/// that can no longer be refreshed (401: revoked, its token spent or unknown; or a reply that does not authorize it) is
/// counted once (<c>refresh:&lt;code or state&gt;</c>) and replaced by a fresh sign-in (<see cref="Bot.SignInAgainAsync"/>),
/// taking a slot of <paramref name="signIns"/>, the runner's bound on concurrent sign-ins; a bot whose sign-in fails
/// gives up for good. Those sign-ins run on a queue of their own, beside the passes: a pass only refreshes, so bulk
/// sign-ins waiting for a slot never hold up the other bots' refreshes.
/// </summary>
public sealed class ContextRefresher(ApiClient api, Func<IReadOnlyCollection<Bot>> bots, BotMetrics metrics,
    SemaphoreSlim signIns)
{
    private static readonly TimeSpan s_pass = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_maxJitter = TimeSpan.FromSeconds(20);

    /// <summary>The sign-out of a context refreshed after its bot let go of it, on its own clock.</summary>
    private static readonly TimeSpan s_logoutTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How many refreshes run at once; a pass holds them all before the next. Also the most queued sign-ins running at
    /// once (the queue itself is unbounded), each still waiting for a slot of the shared sign-in bound.
    /// </summary>
    private const int Concurrency = 16;

    private readonly ConditionalWeakTable<Bot, StrongBox<TimeSpan>> _jitter = [];

    /// <summary>Bots whose context cannot be refreshed, waiting to sign in again, with that context.</summary>
    private readonly Channel<(Bot Bot, GameContext Context)> _signInQueue =
        Channel.CreateUnbounded<(Bot Bot, GameContext Context)>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>The bots queued or signing in again: passes leave them alone, and none is queued twice.</summary>
    private readonly ConcurrentDictionary<Bot, byte> _signingIn = new();

    /// <summary>The re-sign-in loop's token, set when <see cref="RunAsync"/> starts: once cancelled, nothing is queued.</summary>
    private CancellationToken _signInLoop;

    /// <summary>
    /// The loop that signs queued bots in again, set when <see cref="RunAsync"/> starts: it completes once
    /// <c>signInsAgain</c> (or the refresher's own token) is cancelled and the sign-ins it runs are done.
    /// </summary>
    public Task SigningInAgain { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Refreshes the bots due every 5 seconds until <paramref name="ct"/> is cancelled; the task then completes.
    /// Cancelling <paramref name="signInsAgain"/> stops only the re-sign-ins (await <see cref="SigningInAgain"/>), so
    /// the passes go on refreshing bots that are leaving while no new context lands; a context that becomes terminal
    /// after it is counted and left as it is.
    /// </summary>
    public async Task RunAsync(CancellationToken ct, CancellationToken signInsAgain)
    {
        using var signInLoop = CancellationTokenSource.CreateLinkedTokenSource(ct, signInsAgain);
        _signInLoop = signInLoop.Token;
        Task signingIn = SigningInAgain = SignInAgainLoopAsync(signInLoop.Token);
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

        await signingIn;
    }

    private async Task PassAsync(CancellationToken ct)
    {
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var due = new List<(Bot Bot, GameContext Context)>();
            foreach (Bot bot in bots())
            {
                // A bot leaving signs its context out; one stopped has none; one signing in again gets a new one.
                if (bot.State is BotState.Leaving or BotState.Stopped || bot.Context is not { } context ||
                    _signingIn.ContainsKey(bot))
                {
                    continue;
                }

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

            // The bot let go of the context while it was refreshed (a leave signed it out, or it gave up or signed in
            // again). A sign-out sent with the credential this refresh replaced finds nothing on the server, so the
            // new one would stay live for 5 minutes: it is signed out here. Whichever finishes last, nothing stays
            // live: a sign-out read after the rotation sends the new credential itself, and one sent before it is
            // followed by this one (a second sign-out of a context already gone is answered as such).
            if (!ReferenceEquals(bot.Context, context)) await SignOutAsync(bot, context);
        }
        catch (Exception error) when (!(error is OperationCanceledException && ct.IsCancellationRequested))
        {
            // A context the bot let go of since (a leave signs it out) is not a failure.
            if (!ReferenceEquals(bot.Context, context) || bot.State is BotState.Leaving or BotState.Stopped) return;

            // Terminal: a 401, or a reply that answered without authorizing the context. Counted once, then queued.
            if (error is ApiException refused && (refused.Status == 401 || refused.State is not null))
            {
                string code = refused.State ?? refused.Detail;
                metrics.SignInFailed($"refresh:{code}");
                if (_signInLoop.IsCancellationRequested)
                {
                    // The re-sign-ins have stopped (the bots are leaving): the context is left as it is.
                    bot.Note?.Invoke($"The game context cannot be refreshed ({code}); left as it is, the bots are leaving.");
                    return;
                }

                bot.Note?.Invoke($"The game context cannot be refreshed ({code}); signing in again.");
                if (_signingIn.TryAdd(bot, 0)) _signInQueue.Writer.TryWrite((bot, context));
                return;
            }

            metrics.SignInFailed(error is ApiException ? "refresh" : "refresh:unexpected");
            bot.Note?.Invoke($"Refreshing the game context failed: {error.Message}; trying again in {s_pass.TotalSeconds:0} s.");
        }
    }

    /// <summary>
    /// Signs the queued bots in again, up to <see cref="Concurrency"/> at once, each waiting for a slot of the shared
    /// sign-in bound, until <paramref name="ct"/> is cancelled.
    /// </summary>
    private async Task SignInAgainLoopAsync(CancellationToken ct)
    {
        try
        {
            await Parallel.ForEachAsync(_signInQueue.Reader.ReadAllAsync(ct),
                new ParallelOptions { MaxDegreeOfParallelism = Concurrency, CancellationToken = ct },
                (entry, token) => new ValueTask(SignInAgainAsync(entry.Bot, entry.Context, token)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task SignInAgainAsync(Bot bot, GameContext context, CancellationToken ct)
    {
        try
        {
            await signIns.WaitAsync(ct);
            try
            {
                // Left alone when the bot let go of that context while it waited: a leave signs it out.
                if (ReferenceEquals(bot.Context, context) && bot.State is not (BotState.Leaving or BotState.Stopped))
                    await bot.SignInAgainAsync(ct);
            }
            finally
            {
                signIns.Release();
            }
        }
        catch (Exception error) when (!(error is OperationCanceledException && ct.IsCancellationRequested))
        {
            // A sign-in's own failures are the bot's (counted, and it gives up); anything else is counted here.
            metrics.SignInFailed("refresh:unexpected");
            bot.Note?.Invoke($"Signing in again failed: {error.Message}.");
        }
        finally
        {
            _signingIn.TryRemove(bot, out _);
        }
    }

    /// <summary>Signs out a context its bot let go of, best effort, on its own 5 s timeout.</summary>
    private async Task SignOutAsync(Bot bot, GameContext context)
    {
        using var limit = new CancellationTokenSource(s_logoutTimeout);
        try
        {
            await api.LogoutAsync(context, limit.Token);
        }
        catch (Exception error) when (error is ApiException || (error is OperationCanceledException && limit.IsCancellationRequested))
        {
            bot.Note?.Invoke($"Signing out a context refreshed after the bot let go of it failed: {error.Message}.");
        }
    }

    private TimeSpan Jitter(Bot bot) =>
        _jitter.GetValue(bot, static _ => new StrongBox<TimeSpan>(s_maxJitter * Random.Shared.NextDouble())).Value;
}
