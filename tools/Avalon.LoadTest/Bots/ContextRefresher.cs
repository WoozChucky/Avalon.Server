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
/// <remarks>
/// No context the refresher touched stays live once its bot let go of it (a leave signs out the context it holds, with
/// the credential it reads then). A refresh the server applied after the bot's sign-out read the credential leaves a
/// new one nobody signs out: so a refresh, once sent, is seen through whatever stops the refresher; a new credential
/// learned for a bot that let go is signed out here; a refresh with no answer is sent again under the same key until
/// one comes (the server's 30 s receipt replays what it did, and past it the spent token is refused and the context
/// revoked); and a context replaced by a sign-in is signed out.
/// </remarks>
/// <param name="signOuts">The ramp's sign-out breaker, shared with its bots.</param>
public sealed class ContextRefresher(ApiClient api, Func<IReadOnlyCollection<Bot>> bots, BotMetrics metrics,
    SemaphoreSlim signIns, SignOutBreaker signOuts)
{
    private static readonly TimeSpan s_pass = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_maxJitter = TimeSpan.FromSeconds(20);

    /// <summary>
    /// When the refresher stops, the contexts still unsettled are all sent again within this one deadline, each with a
    /// refresh's own attempts (8 s each, two retries) inside it; the sign-outs that follow have their own clock.
    /// </summary>
    private static readonly TimeSpan s_settleBudget = TimeSpan.FromSeconds(60);

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

    /// <summary>
    /// Contexts whose last refresh failed without a final answer (no reply in time, a transport failure, a 5xx, ...),
    /// with their bot: the server may have rotated them, and a sign-out with the credential held then finds nothing.
    /// Each is refreshed again under the same key: by the next pass while its bot holds it, at once when the bot let go
    /// of it, and when the refresher stops.
    /// </summary>
    private readonly ConcurrentDictionary<GameContext, Bot> _unsettled = new();

    /// <summary>The re-sign-in loop's token, set when <see cref="RunAsync"/> starts: once cancelled, nothing is queued.</summary>
    private CancellationToken _signInLoop;

    /// <summary>
    /// The loop that signs queued bots in again, set when <see cref="RunAsync"/> starts: it completes once
    /// <c>signInsAgain</c> (or the refresher's own token) is cancelled and the sign-ins it runs are done. The bots still
    /// queued then are left as they are: the leave signs out the context each holds.
    /// </summary>
    public Task SigningInAgain { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Refreshes the bots due every 5 seconds until <paramref name="ct"/> is cancelled; the pass running then is seen
    /// through (each refresh is bounded by its attempts, 8 s each, and two retries), the contexts whose refresh got no
    /// final answer are refreshed again (<see cref="_unsettled"/>), and the task completes. Cancelling
    /// <paramref name="signInsAgain"/> stops only the re-sign-ins (await <see cref="SigningInAgain"/>), so the passes go
    /// on refreshing bots that are leaving while no new context lands; a context that becomes terminal after it is
    /// counted and left as it is.
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
                // Not cancelled by ct: a refresh dropped mid-way, the server having rotated the context already, would
                // leave a credential nobody learns, so a leaving bot's sign-out would miss it.
                await PassAsync();
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }

        await signingIn;
        await SettleAsync();
    }

    private async Task PassAsync()
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

            await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = Concurrency },
                (entry, _) => new ValueTask(RefreshAsync(entry.Bot, entry.Context)));
        }
        catch (Exception)
        {
            // Whatever went wrong with this pass, the next one runs.
            metrics.SignInFailed("refresh:unexpected");
        }
    }

    private async Task RefreshAsync(Bot bot, GameContext context)
    {
        // Let go of since the pass picked it (its leave signs out the credential it reads): nothing is sent. One whose
        // last refresh got no final answer is settled when the refresher stops.
        if (LetGo(bot, context)) return;

        try
        {
            await api.RefreshAsync(context, CancellationToken.None);
            _unsettled.TryRemove(context, out _);

            // The bot let go of the context while it was refreshed (a leave signed it out, or it gave up or signed in
            // again). A sign-out sent with the credential this refresh replaced finds nothing on the server, so the
            // new one would stay live for 5 minutes: it is signed out here. Whichever finishes last, nothing stays
            // live: a sign-out read after the rotation sends the new credential itself, and one sent before it is
            // followed by this one (a second sign-out of a context already gone is answered as such).
            if (!ReferenceEquals(bot.Context, context)) await SignOutAsync(bot, context);
        }
        catch (ApiException refused) when (IsTerminal(refused))
        {
            _unsettled.TryRemove(context, out _);

            // A context the bot let go of since is not a failure. One answered without authorization was rotated (its
            // reply's credential is in the context now): signed out here when the bot's own sign-out may have read
            // the credential before.
            if (LetGo(bot, context))
            {
                if (refused.State is not null && !ReferenceEquals(bot.Context, context)) await SignOutAsync(bot, context);
                return;
            }

            string code = refused.State ?? refused.Detail;
            metrics.SignInFailed($"refresh:{code}");
            if (_signInLoop.IsCancellationRequested)
            {
                LeftAsItIs(bot, code);
                return;
            }

            if (!_signingIn.TryAdd(bot, 0)) return;
            if (_signInQueue.Writer.TryWrite((bot, context)))
            {
                bot.Note?.Invoke($"The game context cannot be refreshed ({code}); signing in again.");
                return;
            }

            // The queue closed since the check above (the re-sign-ins stopped): nothing signs the bot in again.
            _signingIn.TryRemove(bot, out _);
            LeftAsItIs(bot, code);
        }
        catch (Exception error)
        {
            // No final answer: the server may have rotated the context.
            _unsettled[context] = bot;
            if (LetGo(bot, context))
            {
                // Nobody would send this refresh again (passes leave such a bot alone): it is sent again at once.
                await ResendAsync(bot, context, CancellationToken.None);
                return;
            }

            metrics.SignInFailed(error is ApiException ? "refresh" : "refresh:unexpected");
            bot.Note?.Invoke($"Refreshing the game context failed: {error.Message}; trying again in {s_pass.TotalSeconds:0} s.");
        }
    }

    /// <summary>
    /// Sends again, under the same key with a refresh's own attempts (8 s each, two retries, about 25 s), the refresh of
    /// a context whose refresh got no final answer, now that its bot let go of it or the refresher stops (then within
    /// <paramref name="ct"/>, the settle's deadline): within the server's 30 s receipt it is answered with what the
    /// earlier one did; past it the spent refresh token is refused (401) and the context revoked; a refresh that never
    /// landed rotates the context now. A credential learned is signed out when the bot let go of the context (a bot
    /// still holding it signs out what it reads); a 401 means the context is gone already.
    /// </summary>
    private async Task ResendAsync(Bot bot, GameContext context, CancellationToken ct)
    {
        try
        {
            await api.RefreshAsync(context, ct);
            _unsettled.TryRemove(context, out _);
            if (!ReferenceEquals(bot.Context, context)) await SignOutAsync(bot, context);
        }
        catch (ApiException refused) when (IsTerminal(refused))
        {
            _unsettled.TryRemove(context, out _);
            if (refused.State is not null && !ReferenceEquals(bot.Context, context)) await SignOutAsync(bot, context);
        }
        catch (Exception error) when (error is ApiException || (error is OperationCanceledException && ct.IsCancellationRequested))
        {
            // Still unsettled: sent again when the refresher stops, else reported there.
            bot.Note?.Invoke($"Refreshing a context the bot let go of again failed: {error.Message}.");
        }
    }

    /// <summary>
    /// When the refresher stops (the bots have left): every context whose refresh never got a final answer is refreshed
    /// again (<see cref="ResendAsync"/>), 16 at once, all within one 60 s deadline (a resend not done by then is
    /// cancelled, one not started fails at once), so none the server rotated stays live unknown to its bot's sign-out
    /// and an API that does not answer holds the stop up for 60 s, plus a last sign-out's 10 s, at most.
    /// </summary>
    private async Task SettleAsync()
    {
        KeyValuePair<GameContext, Bot>[] unsettled = [.. _unsettled];
        if (unsettled.Length == 0) return;

        using var deadline = new CancellationTokenSource(s_settleBudget);
        await Parallel.ForEachAsync(unsettled, new ParallelOptions { MaxDegreeOfParallelism = Concurrency },
            (entry, _) => new ValueTask(ResendAsync(entry.Value, entry.Key, deadline.Token)));
        foreach ((GameContext _, Bot bot) in _unsettled)
        {
            bot.Note?.Invoke("A game context's last refresh never got an answer; it may stay live until it expires (5 minutes).");
        }
    }

    /// <summary>
    /// Signs the queued bots in again, up to <see cref="Concurrency"/> at once, each waiting for a slot of the shared
    /// sign-in bound, until <paramref name="ct"/> is cancelled. The queue is then closed and the bots still in it are
    /// left as they are: each still holds its context, which its leave signs out.
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
        finally
        {
            // A refresh queueing after this finds the queue closed and leaves its bot as it is (RefreshAsync).
            _signInQueue.Writer.TryComplete();
            while (_signInQueue.Reader.TryRead(out (Bot Bot, GameContext Context) entry))
            {
                _signingIn.TryRemove(entry.Bot, out _);
                entry.Bot.Note?.Invoke("Not signed in again: left as it is, the bots are leaving.");
            }
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
                if (!LetGo(bot, context))
                {
                    await bot.SignInAgainAsync(ct);

                    // The context replaced, or dropped when the bot gave up, is signed out: a refused one (401) is
                    // gone already, but one answered without authorization was rotated and is still live.
                    if (!ReferenceEquals(bot.Context, context)) await SignOutAsync(bot, context);
                }
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

    /// <summary>Signs out a context its bot let go of, best effort, through the shared breaker on its own 10 s.</summary>
    private async Task SignOutAsync(Bot bot, GameContext context)
    {
        SignOutOutcome outcome = await signOuts.SignOutAsync(api, context);
        if (outcome.Failure is { } failure) bot.Note?.Invoke($"Signing out a context the bot let go of failed: {failure}.");
    }

    /// <summary>
    /// A refresh answered for good: 401 (revoked, its token spent or unknown), or a reply that does not authorize the
    /// context (rotated all the same, its credential kept, <see cref="ApiClient.RefreshAsync"/>).
    /// </summary>
    private static bool IsTerminal(ApiException refused) => refused.Status == 401 || refused.State is not null;

    /// <summary>The bot no longer holds <paramref name="context"/>, or is leaving (its leave signs out what it holds).</summary>
    private static bool LetGo(Bot bot, GameContext context) =>
        !ReferenceEquals(bot.Context, context) || bot.State is BotState.Leaving or BotState.Stopped;

    private static void LeftAsItIs(Bot bot, string code) =>
        bot.Note?.Invoke($"The game context cannot be refreshed ({code}); left as it is, the bots are leaving.");

    private TimeSpan Jitter(Bot bot) =>
        _jitter.GetValue(bot, static _ => new StrongBox<TimeSpan>(s_maxJitter * Random.Shared.NextDouble())).Value;
}
