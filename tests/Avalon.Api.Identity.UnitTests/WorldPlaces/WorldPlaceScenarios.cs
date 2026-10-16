using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.WorldPlaces;

/// <summary>The real ids one run of the suite uses for its logical worlds 1 and 2 and accounts 1 to 16.</summary>
public sealed record PlaceIds(ushort FirstWorld, long FirstAccount)
{
    public ushort World(int n) => (ushort)(FirstWorld + n - 1);
    public long Account(int n) => FirstAccount + n - 1;
}

/// <summary>
/// The place ledger and the queue (#967): one table of scenarios run against every <see cref="IWorldPlaceStore"/>, the
/// in-memory one in CI and the Redis scripts on a throwaway Redis before a PR (<c>WorldPlaceStoreShould</c>).
/// </summary>
public static class WorldPlaceScenarios
{
    public static TheoryData<string> Names => new(s_rows.Keys);

    public static Task RunAsync(string name, Func<FakeTimeProvider, WorldPlacesConfiguration, IWorldPlaceStore> create,
        PlaceIds ids)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        var config = new WorldPlacesConfiguration();
        return s_rows[name](new Harness(clock, config, create(clock, config), ids));
    }

    /// <summary>The calls identity makes, with the times it uses.</summary>
    public sealed class Harness(FakeTimeProvider clock, WorldPlacesConfiguration config, IWorldPlaceStore store, PlaceIds ids)
    {
        private DateTime Now => clock.GetUtcNow().UtcDateTime;
        public void Advance(double seconds) => clock.Advance(TimeSpan.FromSeconds(seconds));

        public Task<PlaceDecision> Join(int account, int? limit, bool staff = false, int world = 1, int? cap = null) =>
            store.EnterAsync(new PlaceRequest(ids.World(world), ids.Account(account),
                Now.Add(GameAuthPolicy.JoinTicketLifetime).Add(GameAuthPolicy.TicketPlaceMargin), limit, staff,
                cap ?? config.QueueCapacity), CancellationToken.None);

        public Task<QueueCheckIn> CheckIn(int account, int? limit, bool available = true, int world = 1) =>
            store.CheckInAsync(ids.World(world), ids.Account(account), limit, available, CancellationToken.None);

        public Task Renew(int account, int world = 1, bool leaveOthers = false) =>
            store.RenewAsync(ids.World(world), ids.Account(account), Now.Add(GameAuthPolicy.SessionLeaseLifetime),
                leaveOthers, CancellationToken.None);

        public Task End(int account, PlaceEnd end, int world = 1) =>
            store.EndAsync(ids.World(world), ids.Account(account), end, Now.Add(config.HeldPlace), CancellationToken.None);

        public Task<bool> Leave(int account, int world = 1) =>
            store.LeaveAsync(ids.World(world), ids.Account(account), CancellationToken.None);

        /// <summary>Places taken, waiters, held places.</summary>
        public async Task<(int Taken, int Queued, int Held)> Sizes(int world = 1)
        {
            WorldPlaceCounts counts = await store.CountAsync(ids.World(world), CancellationToken.None);
            return (counts.Taken, counts.Queued, counts.Held);
        }

        /// <summary>An account that joined and was admitted, its session activated.</summary>
        public async Task Admitted(int account, int? limit, int world = 1)
        {
            Assert.Equal(PlaceOutcome.Admitted, (await Join(account, limit, world: world)).Outcome);
            await Renew(account, world, leaveOthers: true);
        }

        /// <summary>The ticket of a join made <paramref name="secondsAgo"/> seconds ago was not issued after all.</summary>
        public Task Withdraw(int account, double secondsAgo = 0, int world = 1) =>
            store.EndAsync(ids.World(world), ids.Account(account), PlaceEnd.Withdraw,
                Now.AddSeconds(-secondsAgo).Add(GameAuthPolicy.JoinTicketLifetime).Add(GameAuthPolicy.TicketPlaceMargin),
                CancellationToken.None);

        public async Task Queued(int account, int? limit, int position, int world = 1)
        {
            PlaceDecision decision = await Join(account, limit, world: world);
            Assert.Equal((PlaceOutcome.Queued, position), (decision.Outcome, decision.Position));
        }

        public async Task Expect(QueueState state, int position, int account, int? limit, bool available = true)
        {
            QueueCheckIn checkIn = await CheckIn(account, limit, available);
            Assert.Equal((state, position), (checkIn.State, checkIn.Position));
        }
    }

    private static readonly Dictionary<string, Func<Harness, Task>> s_rows = new(StringComparer.Ordinal)
    {
        ["a free place admits"] = async h =>
        {
            PlaceDecision decision = await h.Join(1, limit: 2);
            Assert.Equal((PlaceOutcome.Admitted, PlaceSource.Taken), (decision.Outcome, decision.Source));
            Assert.Equal((1, 0, 0), await h.Sizes());
        },
        ["a full world queues in arrival order"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            await h.Queued(3, limit: 1, position: 2);
            Assert.Equal((1, 2, 0), await h.Sizes());
        },
        ["a new arrival does not jump the line"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            await h.End(1, PlaceEnd.Remove);
            await h.Queued(3, limit: 1, position: 2);
            await h.Expect(QueueState.YourTurn, 0, account: 2, limit: 1);
        },
        ["the front takes the place only at its own check-in"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            await h.End(1, PlaceEnd.Remove);
            Assert.Equal((0, 1, 0), await h.Sizes());
            h.Advance(3);
            await h.Expect(QueueState.YourTurn, 0, account: 2, limit: 1);
            PlaceDecision entered = await h.Join(2, limit: 1);
            Assert.Equal((PlaceOutcome.Admitted, PlaceSource.Turn), (entered.Outcome, entered.Source));
            Assert.Equal(TimeSpan.FromSeconds(3), entered.Waited);
        },
        ["a silent waiter at the head is removed and the next takes the turn"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            await h.Queued(3, limit: 1, position: 2);
            await h.End(1, PlaceEnd.Remove);
            // Account 2 never checks in again; account 3 checks in every 5 s, as told.
            h.Advance(5);
            await h.Expect(QueueState.Queued, 2, account: 3, limit: 1);
            h.Advance(5);
            await h.Expect(QueueState.Queued, 2, account: 3, limit: 1);
            h.Advance(5); // account 2's 15 s are up: the sweep of this check-in removes it first
            QueueCheckIn turn = await h.CheckIn(3, limit: 1);
            Assert.Equal((QueueState.YourTurn, 1), (turn.State, turn.Sweep.WaitersTimedOut));
            await h.Expect(QueueState.NotQueued, 0, account: 2, limit: 1);
        },
        ["a waiter told the slow interval keeps its 45 s when it moves into the first ten"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            for (int account = 2; account <= 12; account++) await h.Queued(account, limit: 1, position: account - 1);
            for (int account = 2; account <= 11; account++) Assert.True(await h.Leave(account));
            // Account 12 joined at position 11, told 15 s and given 45; first now, it is still in the line 30 s later.
            h.Advance(30);
            Assert.Equal((1, 1, 0), await h.Sizes());
            await h.Expect(QueueState.Queued, 1, account: 12, limit: 1);
        },
        ["an unused turn expires and the next takes it"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            await h.Queued(3, limit: 1, position: 2);
            await h.End(1, PlaceEnd.Remove);
            await h.Expect(QueueState.YourTurn, 0, account: 2, limit: 1);
            for (int i = 0; i < 8; i++)
            {
                h.Advance(5);
                await h.Expect(QueueState.Queued, 1, account: 3, limit: 1);
            }

            h.Advance(5); // 45 s: account 2's turn is over
            QueueCheckIn next = await h.CheckIn(3, limit: 1);
            Assert.Equal((QueueState.YourTurn, 1), (next.State, next.Sweep.TurnsExpired));
            await h.Queued(2, limit: 1, position: 1);
        },
        ["a return within the grace goes straight in"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.End(1, PlaceEnd.Hold);
            Assert.Equal((1, 0, 1), await h.Sizes());
            await h.Queued(2, limit: 1, position: 1);
            h.Advance(60);
            PlaceDecision back = await h.Join(1, limit: 1);
            Assert.Equal((PlaceOutcome.Admitted, PlaceSource.Returned), (back.Outcome, back.Source));
            Assert.Equal(0, (await h.Sizes()).Held);
        },
        ["after the grace a returning player queues"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.End(1, PlaceEnd.Hold);
            await h.Queued(2, limit: 1, position: 1);
            for (int i = 0; i < 8; i++)
            {
                h.Advance(10);
                await h.Expect(QueueState.Queued, 1, account: 2, limit: 1);
            }

            h.Advance(10); // 90 s: the held place is gone
            await h.Expect(QueueState.YourTurn, 0, account: 2, limit: 1);
            await h.Queued(1, limit: 1, position: 1);
        },
        ["a goodbye frees the place at once"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.End(1, PlaceEnd.Remove);
            Assert.Equal(PlaceOutcome.Admitted, (await h.Join(2, limit: 1)).Outcome);
        },
        ["a takeover ticket keeps the place through the end of the session it replaces"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            h.Advance(10);
            await h.Renew(1); // a heartbeat of the session the ticket will replace
            Assert.Equal(PlaceSource.Kept, (await h.Join(1, limit: 1)).Source);
            await h.End(1, PlaceEnd.Remove); // the old session ends before the ticket is redeemed
            Assert.Equal((1, 0, 0), await h.Sizes());
            await h.Queued(2, limit: 1, position: 1);
            await h.Renew(1, leaveOthers: true); // the ticket's session activates, which spends the ticket's hold
            await h.End(1, PlaceEnd.Remove);
            Assert.Equal(0, (await h.Sizes()).Taken);
        },
        ["a ticket not issued gives back the place it took, and only that"] = async h =>
        {
            Assert.Equal(PlaceSource.Taken, (await h.Join(1, limit: 1)).Source);
            await h.Withdraw(1);
            Assert.Equal((0, 0, 0), await h.Sizes());
            Assert.Equal(PlaceSource.Taken, (await h.Join(1, limit: 1)).Source);
            h.Advance(1);
            Assert.Equal(PlaceSource.Kept, (await h.Join(1, limit: 1)).Source); // a second ticket since holds the place
            await h.Withdraw(1, secondsAgo: 1); // the first ticket's withdrawal
            Assert.Equal(1, (await h.Sizes()).Taken);
        },
        ["a takeover keeps the same place"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            PlaceDecision again = await h.Join(1, limit: 1);
            Assert.Equal((PlaceOutcome.Admitted, PlaceSource.Kept), (again.Outcome, again.Source));
            Assert.Equal((1, 0, 0), await h.Sizes());
        },
        ["staff always enter"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            Assert.Equal(PlaceOutcome.Admitted, (await h.Join(3, limit: 1, staff: true)).Outcome);
            Assert.Equal((2, 1, 0), await h.Sizes());
        },
        ["the limit raised lets waiters in at their check-in"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            await h.Queued(3, limit: 1, position: 2);
            await h.Expect(QueueState.YourTurn, 0, account: 2, limit: 3);
            await h.Expect(QueueState.YourTurn, 0, account: 3, limit: 3);
        },
        ["the limit lowered removes nobody and admits nobody new"] = async h =>
        {
            for (int account = 1; account <= 3; account++) await h.Admitted(account, limit: 3);
            await h.Queued(4, limit: 1, position: 1);
            Assert.Equal((3, 1, 0), await h.Sizes());
            await h.End(1, PlaceEnd.Remove);
            await h.Expect(QueueState.Queued, 1, account: 4, limit: 1);
            await h.End(2, PlaceEnd.Remove);
            await h.End(3, PlaceEnd.Remove);
            await h.Expect(QueueState.YourTurn, 0, account: 4, limit: 1);
        },
        ["the queue at its cap answers world full"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            Assert.Equal(PlaceOutcome.Queued, (await h.Join(2, limit: 1, cap: 2)).Outcome);
            Assert.Equal(PlaceOutcome.Queued, (await h.Join(3, limit: 1, cap: 2)).Outcome);
            Assert.Equal(PlaceOutcome.WorldFull, (await h.Join(4, limit: 1, cap: 2)).Outcome);
            Assert.Equal((1, 2, 0), await h.Sizes());
        },
        ["one queue per account"] = async h =>
        {
            await h.Admitted(1, limit: 1, world: 1);
            await h.Admitted(2, limit: 1, world: 2);
            await h.Queued(3, limit: 1, position: 1, world: 1);
            await h.Queued(3, limit: 1, position: 1, world: 2);
            Assert.Equal((1, 0, 0), await h.Sizes(world: 1));
            Assert.Equal(QueueState.NotQueued, (await h.CheckIn(3, limit: 1, world: 1)).State);
        },
        ["a world that is not available freezes its line with positions kept"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            await h.Queued(3, limit: 1, position: 2);
            await h.End(1, PlaceEnd.Remove);
            for (int i = 0; i < 6; i++)
            {
                h.Advance(5);
                await h.Expect(QueueState.Paused, 1, account: 2, limit: 1, available: false);
                await h.Expect(QueueState.Paused, 2, account: 3, limit: 1, available: false);
            }

            await h.Expect(QueueState.YourTurn, 0, account: 2, limit: 1);
        },
        ["after a restart former players join the line behind those waiting"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            await h.End(1, PlaceEnd.Remove); // ended as shutdown or maintenance: removed, never held (decision 10)
            await h.Queued(1, limit: 1, position: 2);
        },
        ["a lost ledger is put back by heartbeats, over the limit if need be"] = async h =>
        {
            await h.Admitted(1, limit: 2);
            await h.Admitted(2, limit: 2);
            h.Advance(46); // no renewal reached the ledger: both places expired, as if it had been lost
            Assert.Equal(PlaceOutcome.Admitted, (await h.Join(3, limit: 1)).Outcome);
            await h.Renew(1);
            await h.Renew(2);
            Assert.Equal((3, 0, 0), await h.Sizes());
            await h.Queued(4, limit: 1, position: 1);
        },
        ["two requests for the last place: exactly one takes it"] = async h =>
        {
            PlaceDecision[] decisions = await Task.WhenAll(Enumerable.Range(1, 16).Select(account => h.Join(account, limit: 1)));
            Assert.Single(decisions, d => d.Outcome == PlaceOutcome.Admitted);
            Assert.Equal(Enumerable.Range(1, 15),
                decisions.Where(d => d.Outcome == PlaceOutcome.Queued).Select(d => d.Position).Order());
        },
        ["activation in another world leaves the first world's place"] = async h =>
        {
            await h.Admitted(1, limit: null, world: 1);
            Assert.Equal(PlaceOutcome.Admitted, (await h.Join(1, limit: null, world: 2)).Outcome);
            await h.Renew(1, world: 2, leaveOthers: true);
            Assert.Equal(0, (await h.Sizes(world: 1)).Taken);
            Assert.Equal(1, (await h.Sizes(world: 2)).Taken);
        },
        ["leaving gives back an unused turn"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.Queued(2, limit: 1, position: 1);
            await h.Queued(3, limit: 1, position: 2);
            await h.End(1, PlaceEnd.Remove);
            await h.Expect(QueueState.YourTurn, 0, account: 2, limit: 1);
            Assert.True(await h.Leave(2));
            Assert.False(await h.Leave(2));
            await h.Expect(QueueState.YourTurn, 0, account: 3, limit: 1);
        },
    };
}
