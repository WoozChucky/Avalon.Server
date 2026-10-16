using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using Avalon.Infrastructure.GameAuth;
using Avalon.Network.Packets.Auth;
using Avalon.Server.World.Handlers;
using Avalon.World;
using Avalon.World.GameAuth;
using Avalon.World.Persistence;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.GameAuth;

/// <summary>
/// The world's admission and heartbeat lanes are process-wide (<c>WorldDatabaseWork</c>): a test that fills one runs
/// alone, so no other test's admission is refused by it.
/// </summary>
[CollectionDefinition(nameof(AdmissionLanes), DisableParallelization = true)]
public sealed class AdmissionLanes;

[Collection(nameof(AdmissionLanes))]
public sealed class GameAdmissionHandlerShould
{
    /// <summary>
    /// A limit on a hang only: the ticket is redeemed and the session keyed on the thread pool, which a loaded test run
    /// can hold for seconds, and the outcome is applied by the next flush, whenever this test's thread gets to it (#960).
    /// So the tests flush until the outcome is there, never for an amount of time (#1019).
    /// </summary>
    private static readonly TimeSpan s_hang = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_only_active_trusted_identity_on_a_tick_continuation(bool packetEncryption)
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.PacketEncryption.Returns(packetEncryption);
        using var connection = WorldAdmissionConnection.Create(server);
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>(); GameSessionLease lease = WorldAdmissionConnection.Lease();
        api.AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new WorldAdmissionResult(lease, null));
        // A 0.3.0 client sends a public key of its own in both modes: it learns the mode from the reply.
        byte[] clientKey = new CryptoManager().GetPublicKey();
        await Handler(api).ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = clientKey }
        });
        Assert.Null(connection.AccountId); Assert.Null(connection.GameplayAuthority);
        await FlushUntil(connection, () => connection.AccountId is not null);
        Assert.Equal(lease.Authority.AccountId, connection.AccountId);
        Assert.Same(lease.Authority, connection.GameplayAuthority);
        Assert.False(connection.IsGameplayAuthorized); // The supported-version handshake still must finish.

        // The reply tells the client how to send (#875): sealed exactly when this world seals.
        SGameAdmissionPacket reply = Serializer.Deserialize<SGameAdmissionPacket>(Assert.Single(connection.Sent).PayloadMemory);
        Assert.Equal(GameAdmissionResult.Accepted, reply.Result);
        Assert.Equal(packetEncryption, reply.PacketEncryption);

        // The key exchange runs only where the world seals (#878): there the session is keyed from the client's key and
        // the reply carries the connection's own; a plain world keys nothing and sends no key.
        if (packetEncryption)
        {
            Assert.Equal(clientKey, connection.CryptoSession!.GetOtherEndPublicKey());
            Assert.Equal(connection.ServerCrypto!.GetPublicKey(), reply.PublicKey);
        }
        else
        {
            Assert.Null(connection.CryptoSession);
            Assert.Empty(reply.PublicKey);
        }
    }
    [Fact]
    public async Task Reject_a_second_admission_before_spending_its_ticket()
    {
        using var connection = WorldAdmissionConnection.Create(); Assert.True(connection.TryBeginAdmission());
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        await Handler(api).ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = new CryptoManager().GetPublicKey() }
        });
        await api.DidNotReceiveWithAnyArgs().AdmitAsync(default!, default, default, default);
        Assert.True(connection.IsClosing); Assert.Null(connection.AccountId);
    }
    public enum ClientKey { Valid, Empty, OffTheCurve }

    /// <summary>
    /// A connection without TLS, or a key of the wrong length on a world that seals, is refused before the ticket is
    /// spent. A key of the right length that is not a P-256 point is found when the session is keyed, after the
    /// redemption: the client is told <c>InvalidRequest</c>, the lease is revoked, and the close is counted as the peer's,
    /// <c>bad_public_key</c>, not taken for a server fault. A world that does not seal runs no key exchange (#878), so
    /// whatever key the client sends there is not read.
    /// </summary>
    [Theory]
    [InlineData(false, ClientKey.Valid, false, false, null)]
    [InlineData(true, ClientKey.Empty, true, false, null)]
    [InlineData(true, ClientKey.Empty, false, true, null)]
    [InlineData(true, ClientKey.OffTheCurve, true, true, "bad_public_key")]
    public async Task Refuse_a_connection_without_tls_or_a_client_key_that_is_not_one(bool tls, ClientKey key,
        bool packetEncryption, bool redeemed, string? violation)
    {
        IWorldServer server = Substitute.For<IWorldServer, IServerBase>();
        server.PacketEncryption.Returns(packetEncryption);
        using var connection = WorldAdmissionConnection.Create(server, tls: tls); IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        GameSessionLease lease = WorldAdmissionConnection.Lease();
        api.AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new WorldAdmissionResult(lease, null));
        byte[] publicKey = new CryptoManager().GetPublicKey();
        if (key == ClientKey.OffTheCurve) publicKey[^1] ^= 0x01;   // the point's y moved: the right length, no longer on P-256
        await Handler(api).ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = key == ClientKey.Empty ? [] : publicKey }
        });
        // The ticket is redeemed, and the session keyed, off the calling thread; the outcome is applied on the tick.
        if (redeemed)
            await FlushUntil(connection, () => connection.AccountId is not null || connection.IsClosing);
        await api.Received(redeemed ? 1 : 0).AdmitAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        bool admitted = redeemed && violation is null;
        Assert.Equal(!admitted, connection.IsClosing);
        Assert.Equal(admitted, connection.GameplayAuthority is not null);
        Assert.Equal(violation is null ? [] : [violation], connection.Violations);
        if (violation is not null)
        {
            Assert.Equal(GameAdmissionResult.InvalidRequest,
                Serializer.Deserialize<SGameAdmissionPacket>(Assert.Single(connection.Sent).PayloadMemory).Result);
            Assert.False(lease.IsActive);
        }
    }
    public enum Redemption { Overloaded, Faulted, WorldUnavailable, WorldNotAllowed, InvalidTicket, ClientOutdated, UnsupportedProtocol, AccountBanned, CharacterNotFound }

    /// <summary>
    /// A redemption that admits no one is answered and closed, and nothing is bound. One the world cannot redeem, because
    /// the admission work queue is full (a reconnect storm) or the redemption threw, is answered <c>ServiceUnavailable</c>
    /// like the identity service's own "try later", and counted (#1050); before, its continuation faulted, so the client
    /// got no reply and the world an Error with a stack per connection. A full queue is answered at once, without a
    /// redemption, writes nothing at Error and no stack, and leaves live players' heartbeats their own lane. A world in
    /// maintenance is "try later" too; a ticket refused, an application never allowed on the world, an account that may
    /// not play or a character not the account's is <c>AuthorizationRequired</c>; a client too old for the world or the
    /// API is <c>UnsupportedClient</c> (#1053), so it is told to update rather than to retry.
    /// </summary>
    [Theory]
    [InlineData(Redemption.Overloaded, GameAdmissionResult.ServiceUnavailable, "world.work.refusals lane=admission")]
    [InlineData(Redemption.Faulted, GameAdmissionResult.ServiceUnavailable, "world.admission.failures")]
    [InlineData(Redemption.WorldUnavailable, GameAdmissionResult.ServiceUnavailable, null)]
    [InlineData(Redemption.WorldNotAllowed, GameAdmissionResult.AuthorizationRequired, null)]
    [InlineData(Redemption.InvalidTicket, GameAdmissionResult.AuthorizationRequired, null)]
    [InlineData(Redemption.ClientOutdated, GameAdmissionResult.UnsupportedClient, null)]
    [InlineData(Redemption.UnsupportedProtocol, GameAdmissionResult.UnsupportedClient, null)]
    [InlineData(Redemption.AccountBanned, GameAdmissionResult.AuthorizationRequired, null)]
    [InlineData(Redemption.CharacterNotFound, GameAdmissionResult.AuthorizationRequired, null)]
    public async Task Answer_and_close_a_redemption_that_admits_no_one(Redemption redemption, GameAdmissionResult expected,
        string? counted)
    {
        var log = new TestLog();
        using var connection = WorldAdmissionConnection.Create(logs: log);
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        api.AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(redemption switch
        {
            Redemption.Faulted => Task.FromException<WorldAdmissionResult>(new InvalidOperationException("a defect")),
            Redemption.WorldUnavailable => Task.FromResult(new WorldAdmissionResult(null, "WORLD_UNAVAILABLE")),
            Redemption.WorldNotAllowed => Task.FromResult(new WorldAdmissionResult(null, "WORLD_NOT_ALLOWED")),
            Redemption.ClientOutdated => Task.FromResult(new WorldAdmissionResult(null, "CLIENT_OUTDATED")),
            Redemption.UnsupportedProtocol => Task.FromResult(new WorldAdmissionResult(null, "UNSUPPORTED_PROTOCOL")),
            Redemption.AccountBanned => Task.FromResult(new WorldAdmissionResult(null, "ACCOUNT_BANNED")),
            Redemption.CharacterNotFound => Task.FromResult(new WorldAdmissionResult(null, "CHARACTER_NOT_FOUND")),
            _ => Task.FromResult(new WorldAdmissionResult(null, "INVALID_TICKET")),
        });
        var counts = new ConcurrentQueue<string>();
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "world-server" && instrument.Name is "world.work.refusals" or "world.admission.failures")
                listener.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            counts.Enqueue(tags.Length == 0 ? instrument.Name : $"{instrument.Name} {tags[0].Key}={tags[0].Value}"));
        meter.Start();

        // Overloaded: the admission lane held full, as a reconnect storm holds it, until the end of the test, by work
        // whose end only this test decides. Filled up to its first refusal right before the admission, with nothing
        // in between to await: what an earlier admission left running can free a slot only before that refusal.
        var held = new TaskCompletionSource<WorldAdmissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var holding = new List<Task<WorldAdmissionResult>>();
        GameAdmissionHandler handler = Handler(api);
        var packet = new CGameAdmissionPacket { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = new CryptoManager().GetPublicKey() };
        try
        {
            while (redemption == Redemption.Overloaded)
            {
                Task<WorldAdmissionResult> work = WorldDatabaseWork.Admission.Run(() => held.Task);
                if (WorldDatabaseWork.IsRefusal(work)) break;
                holding.Add(work);
            }
            counts.Clear(); // the fill's own last try was refused and counted: only the admission's count is asserted
            await handler.ExecuteAsync(new() { Connection = connection, Packet = packet });
            if (redemption == Redemption.Overloaded)
            {
                // Answered at once, with nothing left for the tick, and a live player's heartbeat still runs.
                Assert.True(connection.IsClosing);
                Assert.Equal(1, await WorldDatabaseWork.Heartbeat.Run(() => Task.FromResult(1)).WaitAsync(s_hang));
            }
            await FlushUntil(connection, () => connection.IsClosing);
        }
        finally
        {
            held.SetResult(new WorldAdmissionResult(null, "SERVICE_UNAVAILABLE"));
            await Task.WhenAll(holding).WaitAsync(s_hang);
        }

        Assert.Equal(expected, Serializer.Deserialize<SGameAdmissionPacket>(Assert.Single(connection.Sent).PayloadMemory).Result);
        Assert.Null(connection.AccountId); Assert.Null(connection.GameplayAuthority); Assert.Null(connection.GameSessionLease);
        Assert.Null(connection.Character);
        Assert.Equal(counted is null ? [] : [counted], counts);
        await api.Received(redemption == Redemption.Overloaded ? 0 : 1)
            .AdmitAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        if (redemption == Redemption.Overloaded)
            Assert.DoesNotContain(log.Entries, entry => entry.Level >= LogLevel.Error || entry.Exception is not null);
    }

    public enum Ending { Accepted, Refused, Faulted, ClosedFirst, Overloaded }

    /// <summary>
    /// One source may have at most <c>World:Admission:MaxInFlightPerSource</c> admissions in flight (#1052): one past it
    /// is answered <c>ServiceUnavailable</c> and closed before its ticket is redeemed, and counted, while another source
    /// and an exempt one (the load-test bot PC, here past the cap) are redeemed. Over IPv6 the source is a /64, and its
    /// /56 has a cap of its own (<c>MaxInFlightPerIPv6Block</c>, 2 here): a second /64 of the same /56 is redeemed, a
    /// third is refused. The slots come back however the admission ends: admitted, refused by the identity service, its
    /// redemption faulted, its connection closed before the redemption ended (the slot is held until then, since the
    /// redemption still runs), or the admission lane full, which gives them back at once. A slot that leaked would
    /// refuse the source's next admission.
    /// </summary>
    [Theory]
    [InlineData(Ending.Accepted, false)]
    [InlineData(Ending.Refused, false)]
    [InlineData(Ending.Faulted, false)]
    [InlineData(Ending.ClosedFirst, false)]
    [InlineData(Ending.Overloaded, false)]
    [InlineData(Ending.Refused, true)]
    [InlineData(Ending.Overloaded, true)]
    public async Task Hold_a_source_to_its_admissions_in_flight_and_give_its_slot_back_however_one_ends(Ending ending, bool ipv6)
    {
        // Over IPv6 the neighbour is another /64 of the flooder's /56, and the cousin a third.
        var flooder = IPAddress.Parse(ipv6 ? "2001:db8:0:1::7" : "203.0.113.7");
        var neighbour = IPAddress.Parse(ipv6 ? "2001:db8:0:2::8" : "203.0.113.8");
        var cousin = IPAddress.Parse("2001:db8:0:3::9");
        var bots = IPAddress.Parse("10.10.1.21");
        var sources = new AdmissionSourceBudget(new AdmissionSourceOptions
        {
            MaxInFlightPerSource = 1,
            MaxInFlightPerIPv6Block = 2,
            ExemptSources = ["10.10.1.21/32"],
        });
        // Every redemption waits for this test to end it.
        var redemption = new TaskCompletionSource<WorldAdmissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        api.AdmitAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => redemption.Task);
        var handler = new GameAdmissionHandler(api, sources);
        using SourceRefusals refusals = new();

        using var first = WorldAdmissionConnection.Create(source: flooder);
        using var second = WorldAdmissionConnection.Create(source: flooder);
        using var other = WorldAdmissionConnection.Create(source: neighbour);
        using var bot = WorldAdmissionConnection.Create(source: bots);
        using var anotherBot = WorldAdmissionConnection.Create(source: bots);
        using var third = WorldAdmissionConnection.Create(source: cousin);
        using var again = WorldAdmissionConnection.Create(source: flooder);

        if (ending == Ending.Overloaded)
        {
            // The admission lane held full, as the overload case above holds it: refused at once, without a
            // redemption, and the slot given back with the refusal.
            var lane = new TaskCompletionSource<WorldAdmissionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var holding = new List<Task<WorldAdmissionResult>>();
            try
            {
                while (true)
                {
                    Task<WorldAdmissionResult> work = WorldDatabaseWork.Admission.Run(() => lane.Task);
                    if (WorldDatabaseWork.IsRefusal(work)) break;
                    holding.Add(work);
                }
                await Admit(handler, first);
                Assert.True(first.IsClosing);
                Assert.Equal(GameAdmissionResult.ServiceUnavailable, ReplyOf(first));
            }
            finally
            {
                lane.SetResult(new WorldAdmissionResult(null, "SERVICE_UNAVAILABLE"));
                await Task.WhenAll(holding).WaitAsync(s_hang);
            }
            Assert.Equal(0, sources.InFlight(flooder));
            Assert.Equal(0, sources.InFlightInIPv6Block(flooder));
        }
        else
        {
            await Admit(handler, first);
            Assert.Equal(1, sources.InFlight(flooder));

            // Over the source's budget: answered at once, before its ticket is redeemed; the first keeps its slot.
            await Admit(handler, second);
            Assert.True(second.IsClosing);
            Assert.Equal(GameAdmissionResult.ServiceUnavailable, ReplyOf(second));
            Assert.Equal(1, sources.InFlight(flooder));

            // Another source, and an exempt one past the cap, are not held back by it.
            await Admit(handler, other);
            await Admit(handler, bot);
            await Admit(handler, anotherBot);
            Assert.False(other.IsClosing || bot.IsClosing || anotherBot.IsClosing);
            Assert.Equal(1, sources.InFlight(neighbour));
            Assert.Equal(0, sources.InFlight(bots));
            if (ipv6)
            {
                // The /56 holds the flooder's and the neighbour's: a third /64 of it is refused, and gives back its own.
                Assert.Equal(2, sources.InFlightInIPv6Block(flooder));
                await Admit(handler, third);
                Assert.True(third.IsClosing);
                Assert.Equal(GameAdmissionResult.ServiceUnavailable, ReplyOf(third));
                Assert.Equal(0, sources.InFlight(cousin));
                Assert.Equal(2, sources.InFlightInIPv6Block(flooder));
            }

            if (ending == Ending.ClosedFirst)
            {
                first.Close(false);
                Assert.Equal(1, sources.InFlight(flooder)); // its redemption still runs
            }
            if (ending == Ending.Faulted)
                redemption.SetException(new InvalidOperationException("a defect"));
            else
                redemption.SetResult(ending == Ending.Refused ? new WorldAdmissionResult(null, "INVALID_TICKET") : new WorldAdmissionResult(WorldAdmissionConnection.Lease(), null));
            await Until(() => sources.InFlight(flooder) == 0 && sources.InFlight(neighbour) == 0 &&
                              sources.InFlightInIPv6Block(flooder) == 0);
        }

        // The slot came back: the source's next admission is redeemed.
        redemption.TrySetResult(new WorldAdmissionResult(null, "INVALID_TICKET"));
        await Admit(handler, again);
        await FlushUntil(again, () => again.IsClosing || again.AccountId is not null);
        Assert.Equal(0, sources.InFlight(flooder));
        Assert.Equal(0, sources.InFlightInIPv6Block(flooder));
        Assert.Equal(Enumerable.Repeat("in_flight", ending == Ending.Overloaded ? 0 : ipv6 ? 2 : 1), refusals.Reasons);
        WorldAdmissionConnection[] redeemed = ending == Ending.Overloaded ? [again] : [first, other, bot, anotherBot, again];
        foreach (WorldAdmissionConnection connection in redeemed)
            await api.Received(1).AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        WorldAdmissionConnection[] refused = ending == Ending.Overloaded ? [first] : ipv6 ? [second, third] : [second];
        foreach (WorldAdmissionConnection connection in refused)
            await api.DidNotReceive().AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A source may start at most <c>World:Admission:MaxPerSourcePerMinute</c> admissions a minute (#1052), 2 here, each
    /// redeemed and ended at once: the third is answered <c>ServiceUnavailable</c> before its ticket is redeemed, counted
    /// as <c>rate</c>, and one more is allowed every 30 s; an exempt source is never held to it. Over IPv6 the source's
    /// /56 has a rate of its own (<c>MaxPerIPv6BlockPerMinute</c>, 3 here), spent with the source's: a second /64 of the
    /// /56 takes its last, and a third /64 is refused though its own budget is whole. A source whose budget is whole
    /// again is the same as one never seen, and is swept from the table, so it cannot grow without bound.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hold_a_source_to_its_admissions_per_minute_and_forget_it_once_idle(bool ipv6)
    {
        var flooder = IPAddress.Parse(ipv6 ? "2001:db8:0:1::7" : "203.0.113.7");
        var bots = IPAddress.Parse("10.10.1.21");
        var clock = new FakeTimeProvider();
        var sources = new AdmissionSourceBudget(new AdmissionSourceOptions
        {
            MaxPerSourcePerMinute = 2,
            MaxPerIPv6BlockPerMinute = 3,
            ExemptSources = ["10.10.1.21/32"],
        }, clock);
        IGameAdmissionClient api = Substitute.For<IGameAdmissionClient>();
        api.AdmitAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new WorldAdmissionResult(null, "INVALID_TICKET"));
        var handler = new GameAdmissionHandler(api, sources);
        using SourceRefusals refusals = new();

        // The flooder's four, the bot PC's three, a source elsewhere, and two more /64s of the flooder's /56.
        WorldAdmissionConnection[] connections = [.. Enumerable.Range(0, 4).Select(_ => WorldAdmissionConnection.Create(source: flooder)),
            .. Enumerable.Range(0, 3).Select(_ => WorldAdmissionConnection.Create(source: bots)),
            WorldAdmissionConnection.Create(source: IPAddress.Parse("203.0.113.8")),
            WorldAdmissionConnection.Create(source: IPAddress.Parse("2001:db8:0:2::8")),
            WorldAdmissionConnection.Create(source: IPAddress.Parse("2001:db8:0:3::9"))];
        List<WorldAdmissionConnection> refused = [connections[2]];
        try
        {
            foreach (WorldAdmissionConnection connection in connections[..2].Concat(connections[4..7]))
            {
                await Admit(handler, connection);
                Assert.False(connection.IsClosing);
            }

            await Admit(handler, connections[2]);
            Assert.True(connections[2].IsClosing);
            Assert.Equal(GameAdmissionResult.ServiceUnavailable, ReplyOf(connections[2]));

            if (ipv6)
            {
                await Admit(handler, connections[8]);
                Assert.False(connections[8].IsClosing);
                await Admit(handler, connections[9]);
                Assert.True(connections[9].IsClosing);
                Assert.Equal(GameAdmissionResult.ServiceUnavailable, ReplyOf(connections[9]));
                refused.Add(connections[9]);
            }

            clock.Advance(TimeSpan.FromSeconds(30));
            await Admit(handler, connections[3]);
            Assert.False(connections[3].IsClosing);

            // Two minutes on, every budget is whole: the next admission's sweep drops them all.
            clock.Advance(TimeSpan.FromMinutes(2));
            await Admit(handler, connections[7]);
            Assert.Equal(1, sources.RateEntries);

            Assert.Equal(Enumerable.Repeat("rate", refused.Count), refusals.Reasons);
            foreach (WorldAdmissionConnection connection in refused)
                await api.DidNotReceive().AdmitAsync(Arg.Any<string>(), connection.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
            foreach (WorldAdmissionConnection connection in connections.Take(ipv6 ? 9 : 8).Except(refused))
                await Until(() => api.ReceivedCalls().Any(call => call.GetArguments()[1] is Guid id && id == connection.Id));
        }
        finally
        {
            foreach (WorldAdmissionConnection connection in connections)
                connection.Dispose();
        }
    }

    /// <summary>The <c>reason</c> of each <c>world.admission.source_refusals</c> count, in order.</summary>
    private sealed class SourceRefusals : IDisposable
    {
        private readonly ConcurrentQueue<string> _reasons = new();
        private readonly MeterListener _meter = new();

        public SourceRefusals()
        {
            _meter.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "world-server" && instrument.Name == "world.admission.source_refusals")
                    listener.EnableMeasurementEvents(instrument);
            };
            _meter.SetMeasurementEventCallback<long>((_, _, tags, _) =>
                _reasons.Enqueue(tags.Length == 1 && tags[0].Key == "reason" ? (string)tags[0].Value! : "?"));
            _meter.Start();
        }

        public IEnumerable<string> Reasons => _reasons;

        public void Dispose() => _meter.Dispose();
    }

    private static GameAdmissionHandler Handler(IGameAdmissionClient api) =>
        new(api, new AdmissionSourceBudget(new AdmissionSourceOptions()));

    private static Task Admit(GameAdmissionHandler handler, WorldAdmissionConnection connection) =>
        handler.ExecuteAsync(new()
        {
            Connection = connection,
            Packet = new() { JoinTicket = GameAuthCryptography.NewToken(), PublicKey = new CryptoManager().GetPublicKey() }
        });

    private static GameAdmissionResult ReplyOf(WorldAdmissionConnection connection) =>
        Serializer.Deserialize<SGameAdmissionPacket>(Assert.Single(connection.Sent).PayloadMemory).Result;

    /// <summary>Waits until <paramref name="done" /> holds: a slot is given back on the thread pool, where a redemption ends.</summary>
    private static async Task Until(Func<bool> done)
    {
        using var hang = new CancellationTokenSource(s_hang);
        while (!done())
            await Task.Delay(1, hang.Token);
    }

    /// <summary>Runs the connection's tick continuations until <paramref name="settled" /> holds after one.</summary>
    private static async Task FlushUntil(WorldAdmissionConnection connection, Func<bool> settled)
    {
        using var hang = new CancellationTokenSource(s_hang);
        while (true)
        {
            connection.FlushContinuations();
            if (settled())
                return;
            await Task.Delay(1, hang.Token);
        }
    }
}
