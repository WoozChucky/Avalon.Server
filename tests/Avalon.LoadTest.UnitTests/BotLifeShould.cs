using System.Collections.Concurrent;
using System.Diagnostics;
using Avalon.LoadTest.Api;
using Avalon.LoadTest.Bots;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class BotLifeShould
{
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// On a full world a ramp step's bot waits in the line for its turn, for minutes (#967). The step must not wait for
    /// it (it never completed), and the bot's life must take that same entry over rather than start a second one beside
    /// it: two entries at once would ask for two tickets and race each other into the world.
    /// </summary>
    [Fact]
    public async Task Hand_an_entry_waiting_in_the_line_to_the_bots_life_without_entering_twice()
    {
        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bot = new StandInBot(BehaviourKind.Idle)
        {
            Entry = async (queued, ct) =>
            {
                queued?.Invoke();
                await turn.Task.WaitAsync(ct);
            },
        };
        using var stop = new CancellationTokenSource(s_wait * 3);

        Task entry = await BotLife.StartEntryAsync(bot, stop.Token).WaitAsync(s_wait);
        Assert.False(entry.IsCompleted);
        Assert.Equal(BotState.Queued, bot.State);

        Task life = BotLife.RunAsync(bot, new BotMetrics(), stop.Token, entry);
        Assert.Single(bot.Calls);

        turn.SetResult();
        await entry.WaitAsync(s_wait);
        await stop.CancelAsync();
        await life.WaitAsync(s_wait);
        Assert.Equal([("enter", false)], bot.Calls.Select(call => (call.Name, call.Takeover)));
        Assert.Equal(BotState.InWorld, bot.State);
    }

    /// <summary>
    /// A quitter frees its place on a full world only by quitting (the leave, the goodbye and the close), and the line
    /// moves only when it then stays out and joins again as a new player, behind the line: a quit counted as a
    /// disconnect, a return with a takeover, or no time out of the world would each hide what the limit test reads.
    /// </summary>
    [Fact]
    public async Task Quit_stay_out_and_join_again_without_a_takeover()
    {
        var away = TimeSpan.FromMilliseconds(150);
        var bot = new StandInBot(BehaviourKind.Quitter, BotState.InWorld)
        {
            Quits = new QuitTimes(new TimeRange(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)), new TimeRange(away, away)),
        };
        var metrics = new BotMetrics();
        using var stop = new CancellationTokenSource(s_wait * 3);

        Task life = BotLife.RunAsync(bot, metrics, stop.Token);
        await bot.EnteredTwice.WaitAsync(s_wait);
        await stop.CancelAsync();
        await life.WaitAsync(s_wait);

        (string Name, bool Takeover, long At)[] calls = [.. bot.Calls];
        Assert.Equal(["quit", "enter", "quit", "enter"], calls.Take(4).Select(call => call.Name));
        Assert.All(calls.Where(call => call.Name == "enter"), call => Assert.False(call.Takeover));
        for (int i = 1; i < 4; i += 2)
        {
            TimeSpan stayedOut = Stopwatch.GetElapsedTime(calls[i - 1].At, calls[i].At);
            Assert.True(stayedOut >= away * 0.9, $"out of the world for {stayedOut.TotalMilliseconds:0} ms");
        }

        StepClientValues window = metrics.TakeWindow();
        Assert.Equal((calls.Count(call => call.Name == "quit"), 0), (window.Quits, window.Disconnects));
    }

    /// <summary>A <see cref="Bot"/>'s life members, recording what the life asks of it; its connection never closes.</summary>
    private sealed class StandInBot(BehaviourKind behaviour, BotState state = BotState.SignedIn) : ILivingBot
    {
        private readonly TaskCompletionSource _open = new();
        private readonly TaskCompletionSource _enteredTwice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;

        /// <summary>What each entry does before the bot is in the world; nothing by default.</summary>
        public Func<Action?, CancellationToken, Task>? Entry { get; init; }

        public ConcurrentQueue<(string Name, bool Takeover, long At)> Calls { get; } = new();

        public int Index => 0;

        public BotState State { get; private set; } = state;

        public BehaviourKind Behaviour => behaviour;

        public Fighter? Fighter => null;

        public QuitTimes? Quits { get; init; }

        public GameContext? Context { get; } = new("credential", "refresh", DateTimeOffset.MaxValue, DateTimeOffset.MaxValue);

        public CancellationToken GaveUp => CancellationToken.None;

        public Task Closed => _open.Task;

        public Action<string>? Note => null;

        /// <summary>Completes once two entries got the bot in.</summary>
        public Task EnteredTwice => _enteredTwice.Task;

        public async Task EnterAsync(bool takeover, CancellationToken ct, Action? queued = null)
        {
            Calls.Enqueue(("enter", takeover, Stopwatch.GetTimestamp()));
            if (Entry is not null)
            {
                await Entry(() =>
                {
                    State = BotState.Queued;
                    queued?.Invoke();
                }, ct);
            }

            State = BotState.InWorld;
            if (Interlocked.Increment(ref _entered) == 2) _enteredTwice.TrySetResult();
        }

        public Task ChangeCharacterAsync(CancellationToken ct) => throw new InvalidOperationException("Only a churner changes character.");

        public Task DisconnectAsync(CancellationToken ct)
        {
            Calls.Enqueue(("quit", false, Stopwatch.GetTimestamp()));
            State = BotState.SignedIn;
            return Task.CompletedTask;
        }
    }
}
