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

    /// <summary>
    /// The calls identity makes, with the times it uses. Each join is a ticket with its own id (the session it becomes);
    /// a session is named by its fencing token, 1 unless a row says otherwise, and its id is its ticket's.
    /// </summary>
    public sealed class Harness(FakeTimeProvider clock, WorldPlacesConfiguration config, IWorldPlaceStore store, PlaceIds ids)
    {
        private readonly Dictionary<(int Account, int World), Guid> _tickets = [];
        private DateTime Now => clock.GetUtcNow().UtcDateTime;
        public void Advance(double seconds) => clock.Advance(TimeSpan.FromSeconds(seconds));

        /// <summary>The ticket of the account's last join in the world.</summary>
        public Guid Ticket(int account, int world = 1) => _tickets[(account, world)];

        /// <summary>A join, as a new ticket or as <paramref name="ticket"/>, its place ending as if issued <paramref name="secondsAgo"/> seconds ago.</summary>
        public Task<PlaceDecision> Join(int account, int? limit, bool staff = false, int world = 1, int? cap = null,
            Guid? ticket = null, double secondsAgo = 0)
        {
            Guid id = ticket ?? Guid.NewGuid();
            _tickets[(account, world)] = id;
            return store.EnterAsync(new PlaceRequest(ids.World(world), ids.Account(account), TicketPlaceEnd(secondsAgo), limit,
                staff, cap ?? config.QueueCapacity, id), CancellationToken.None);
        }

        public Task<QueueCheckIn> CheckIn(int account, int? limit, bool available = true, int world = 1) =>
            store.CheckInAsync(ids.World(world), ids.Account(account), limit, available, CancellationToken.None);

        /// <summary>A redemption or heartbeat of the session <paramref name="fence"/>, or its activation.</summary>
        public Task Renew(int account, int world = 1, long fence = 1, bool activation = false, Guid? session = null,
            double secondsAgo = 0) =>
            store.RenewAsync(ids.World(world), ids.Account(account), fence,
                session ?? (_tickets.TryGetValue((account, world), out Guid last) ? last : Guid.Empty),
                Now.AddSeconds(-secondsAgo).Add(GameAuthPolicy.SessionLeaseLifetime), activation, CancellationToken.None);

        public Task End(int account, PlaceEnd end, int world = 1, long fence = 1) =>
            store.EndAsync(ids.World(world), ids.Account(account), fence, end, Now.Add(config.HeldPlace), CancellationToken.None);

        /// <summary>The ticket <paramref name="ticket"/> is known not to be issued.</summary>
        public Task Withdraw(int account, Guid ticket, int world = 1) =>
            store.WithdrawAsync(ids.World(world), ids.Account(account), ticket, CancellationToken.None);

        /// <summary>The replay of the ticket <paramref name="ticket"/>, issued <paramref name="secondsAgo"/> seconds ago.</summary>
        public Task Rejoin(int account, Guid ticket, double secondsAgo, int world = 1) =>
            store.RejoinAsync(ids.World(world), ids.Account(account), ticket, TicketPlaceEnd(secondsAgo), CancellationToken.None);

        public Task<bool> Leave(int account, int world = 1) =>
            store.LeaveAsync(ids.World(world), ids.Account(account), CancellationToken.None);

        /// <summary>Places taken, waiters, held places.</summary>
        public async Task<(int Taken, int Queued, int Held)> Sizes(int world = 1)
        {
            WorldPlaceCounts counts = await store.CountAsync(ids.World(world), CancellationToken.None);
            return (counts.Taken, counts.Queued, counts.Held);
        }

        /// <summary>An account that joined and was admitted, its session (<paramref name="fence"/>) activated.</summary>
        public async Task Admitted(int account, int? limit, int world = 1, long fence = 1)
        {
            Assert.Equal(PlaceOutcome.Admitted, (await Join(account, limit, world: world)).Outcome);
            await Renew(account, world, fence, activation: true);
        }

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

        private DateTime TicketPlaceEnd(double secondsAgo) =>
            Now.AddSeconds(-secondsAgo).Add(GameAuthPolicy.JoinTicketLifetime).Add(GameAuthPolicy.TicketPlaceMargin);
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
            await h.Renew(1, fence: 2, activation: true); // the ticket's session activates, which spends its hold
            await h.End(1, PlaceEnd.Remove, fence: 2);
            Assert.Equal(0, (await h.Sizes()).Taken);
        },
        ["a ticket replayed after an issue cancelled once its write landed keeps its place, as that ticket's"] = async h =>
        {
            Assert.Equal(PlaceSource.Taken, (await h.Join(1, limit: 1)).Source); // the client gave up: nothing is withdrawn
            Guid ticket = h.Ticket(1);
            h.Advance(5);
            Assert.Equal(PlaceSource.Kept, (await h.Join(1, limit: 1)).Source); // another request since
            await h.Rejoin(1, ticket, secondsAgo: 5); // the replay
            Assert.Equal((1, 0, 0), await h.Sizes());
            await h.Renew(1, activation: true, session: ticket); // its session activates: the hold is that ticket's to spend
            await h.End(1, PlaceEnd.Remove);
            Assert.Equal(0, (await h.Sizes()).Taken);
        },
        ["a replay never creates a place: a used ticket's leaves the freed place to whoever took it"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            Guid used = h.Ticket(1);
            await h.End(1, PlaceEnd.Remove); // played and logged out
            await h.Admitted(2, limit: 1); // the freed place goes to another
            await h.Rejoin(1, used, secondsAgo: 0); // the used ticket's request replayed within its 30 s
            Assert.Equal((1, 0, 0), await h.Sizes());
            await h.Queued(1, limit: 1, position: 1);
        },
        ["a failed issue gives back its own place, never that of a ticket that entered after it, whatever their ends"] = async h =>
        {
            Assert.Equal(PlaceSource.Taken, (await h.Join(2, limit: 1, world: 2)).Source);
            await h.Withdraw(2, h.Ticket(2, world: 2), world: 2); // a lone ticket's failed issue gives its place back
            Assert.Equal(0, (await h.Sizes(world: 2)).Taken);
            Assert.Equal(PlaceSource.Taken, (await h.Join(1, limit: 1)).Source);
            Guid first = h.Ticket(1);
            // A concurrent request for the same key, entered second, its ticket ending a second earlier; it wins the write.
            Assert.Equal(PlaceSource.Kept, (await h.Join(1, limit: 1, secondsAgo: 1)).Source);
            await h.Withdraw(1, first); // the first one's write failed
            Assert.Equal(1, (await h.Sizes()).Taken);
        },
        ["a failed issue does not remove a place a session's heartbeat now owns"] = async h =>
        {
            Assert.Equal(PlaceSource.Taken, (await h.Join(1, limit: 1)).Source); // the session's own place had been lost
            Guid ticket = h.Ticket(1);
            await h.Renew(1, fence: 3, session: Guid.NewGuid()); // the account's live session heartbeats
            await h.Withdraw(1, ticket); // and the ticket's write fails
            Assert.Equal(1, (await h.Sizes()).Taken);
            await h.End(1, PlaceEnd.Remove, fence: 3);
            Assert.Equal(0, (await h.Sizes()).Taken);
        },
        ["an old session's late end or heartbeat does not remove, hold or shorten the new session's place"] = async h =>
        {
            await h.Admitted(1, limit: 1); // the old session, fencing token 1
            h.Advance(20);
            Assert.Equal(PlaceSource.Kept, (await h.Join(1, limit: 1)).Source); // a takeover
            await h.Renew(1, fence: 2); // redeemed: the new session owns the place, to 65 s
            await h.Renew(1, fence: 2, activation: true);
            await h.Renew(1, fence: 1, secondsAgo: 15); // the old session's heartbeat, sent before the takeover, arrives late
            await h.End(1, PlaceEnd.Remove, fence: 1); // and so do its ends
            await h.End(1, PlaceEnd.Hold, fence: 1);
            Assert.Equal((1, 0, 0), await h.Sizes());
            h.Advance(40); // 60 s: the new session's lease still runs
            Assert.Equal(1, (await h.Sizes()).Taken);
            await h.End(1, PlaceEnd.Remove, fence: 2);
            Assert.Equal(0, (await h.Sizes()).Taken);
        },
        ["an activation spends only its own ticket's hold"] = async h =>
        {
            Assert.Equal(PlaceSource.Taken, (await h.Join(1, limit: 1)).Source);
            Guid first = h.Ticket(1);
            await h.Renew(1, session: first); // redeemed: session 1
            Assert.Equal(PlaceSource.Kept, (await h.Join(1, limit: 1)).Source); // a takeover, asked before session 1 activates
            await h.Renew(1, activation: true, session: first); // session 1 activates
            await h.End(1, PlaceEnd.Remove); // and ends before the takeover is redeemed
            Assert.Equal((1, 0, 0), await h.Sizes());
            await h.Queued(2, limit: 1, position: 1);
            await h.Renew(1, fence: 2, activation: true); // the takeover's session
            await h.End(1, PlaceEnd.Remove, fence: 2);
            Assert.Equal(0, (await h.Sizes()).Taken);
        },
        ["an ended session's late heartbeat changes nothing, and an end never brings back a place that is gone"] = async h =>
        {
            await h.Admitted(1, limit: 3);
            await h.End(1, PlaceEnd.Hold);
            await h.Renew(1); // its own heartbeat, arriving after its end
            Assert.Equal((1, 0, 1), await h.Sizes());
            await h.End(2, PlaceEnd.Hold); // the end of a session whose place is already gone
            Assert.Equal((1, 0, 1), await h.Sizes());
            await h.Renew(1, fence: 2); // a newer session of the held account renews it
            Assert.Equal((1, 0, 0), await h.Sizes());

            await h.Admitted(3, limit: 3, world: 2);
            Assert.Equal(PlaceSource.Kept, (await h.Join(3, limit: 3, world: 2)).Source); // a takeover
            await h.End(3, PlaceEnd.Remove, world: 2); // the old session ends: the takeover's hold keeps the place
            h.Advance(10);
            await h.Renew(3, world: 2); // and the old session's heartbeat arrives after its end
            h.Advance(36); // the takeover's hold is over; that heartbeat's lease would not be
            Assert.Equal(0, (await h.Sizes(world: 2)).Taken);
        },
        ["a ticket asked during a held place's grace never cuts the grace short"] = async h =>
        {
            await h.Admitted(1, limit: 1);
            await h.End(1, PlaceEnd.Hold); // held for 90 s
            Assert.Equal(PlaceSource.Returned, (await h.Join(1, limit: 1)).Source); // a ticket asked at once, never redeemed
            h.Advance(60);
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
            await h.Renew(1, world: 2, fence: 2, activation: true);
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
