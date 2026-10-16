using System.Net;
using System.Net.Http.Json;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.GameAuth;
using Microsoft.Extensions.Time.Testing;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class GameAdmissionClientShould
{
    private static GameAdmissionOptions Options() => new() { ApiUrl = "https://internal.avalon.example/", ServerId = "world-one", WorldId = 1, ClientCertificatePath = "mounted-workload.pfx", ApiCertificateSha256 = new string('A', 64) };
    [Fact]
    public void Refuse_admission_configuration_without_an_explicit_API_certificate_pin()
    {
        GameAdmissionOptions options = Options();
        options.ApiCertificateSha256 = string.Empty;
        Assert.False(options.IsValid());
    }

    private static JoinRedemptionReceipt Receipt(Guid connection, Guid redemption) => new()
    {
        AccountId = "42",
        FencingToken = "7",
        GameSessionId = Guid.NewGuid().ToString("D"),
        GameContextId = Guid.NewGuid().ToString("D"),
        ConnectionId = connection.ToString("D"),
        RedemptionId = redemption.ToString("D"),
        ServerId = "world-one",
        WorldId = 1,
        CredentialsVersion = 3,
        SessionEpoch = "9"
    };
    private static SessionLeaseResponse Active(JoinRedemptionReceipt receipt) => new()
    {
        State = "active",
        AccountId = receipt.AccountId,
        GameSessionId = receipt.GameSessionId,
        GameContextId = receipt.GameContextId,
        FencingToken = receipt.FencingToken,
        ServerId = receipt.ServerId,
        WorldId = receipt.WorldId,
        AccessLevel = 1,
        CredentialsVersion = receipt.CredentialsVersion,
        SessionEpoch = receipt.SessionEpoch,
        LeaseUntil = DateTime.UtcNow.AddSeconds(44),
        AuthorizationUntil = DateTime.UtcNow.AddMinutes(5)
    };
    [Fact]
    public async Task Retry_only_the_exact_redemption_and_never_admit_a_pending_receipt()
    {
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid(); JoinRedemptionReceipt receipt = Receipt(connection, redemption);
        using var handler = new RecordingHandler((call, _) => call switch
        {
            1 => Json(JoinRedemptionReceipt.Failure("IN_PROGRESS"), HttpStatusCode.Conflict),
            2 => Json(receipt),
            _ => Json(Active(receipt))
        });
        using var http = new HttpClient(handler);
        var client = new GameAdmissionClient(http, Options(), TimeProvider.System);
        WorldAdmissionResult result = await client.AdmitAsync(GameAuthCryptography.NewToken(), connection, redemption, CancellationToken.None);
        Assert.NotNull(result.Lease); Assert.Null(result.Error);
        Assert.Equal(handler.Bodies[0], handler.Bodies[1]);
        Assert.Equal("/internal/game/sessions/activate", handler.Paths[2]);
        Assert.Equal(42, result.Lease.Authority.AccountId.Value);
    }
    [Theory]
    [InlineData("server")]
    [InlineData("connection")]
    [InlineData("redemption")]
    [InlineData("world")]
    public async Task Never_activate_a_receipt_for_another_workload_or_connection(string mismatch)
    {
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid(); JoinRedemptionReceipt receipt = Receipt(connection, redemption);
        receipt = mismatch switch
        {
            "server" => receipt with { ServerId = "world-two" },
            "connection" => receipt with { ConnectionId = Guid.NewGuid().ToString("D") },
            "redemption" => receipt with { RedemptionId = Guid.NewGuid().ToString("D") },
            _ => receipt with { WorldId = 2 }
        };
        using var handler = new RecordingHandler((_, _) => Json(receipt));
        using var http = new HttpClient(handler);
        WorldAdmissionResult result = await new GameAdmissionClient(http, Options(), TimeProvider.System).AdmitAsync(GameAuthCryptography.NewToken(), connection, redemption, CancellationToken.None);
        Assert.Null(result.Lease); Assert.Equal("INVALID_ADMISSION", result.Error);
        Assert.Single(handler.Paths);
    }
    /// <summary>
    /// A redemption the identity service refuses keeps its reason when the world maps it (#1053): a permanent refusal
    /// turned into "try later" would have the client retry what never changes. One the world does not know is "try later".
    /// </summary>
    [Theory]
    [InlineData("CLIENT_OUTDATED", "CLIENT_OUTDATED")]
    [InlineData("UNSUPPORTED_PROTOCOL", "UNSUPPORTED_PROTOCOL")]
    [InlineData("WORLD_NOT_ALLOWED", "WORLD_NOT_ALLOWED")]
    [InlineData("ACCOUNT_BANNED", "ACCOUNT_BANNED")]
    [InlineData("ACCOUNT_DEACTIVATED", "ACCOUNT_DEACTIVATED")]
    [InlineData("ACCOUNT_CONSOLIDATING", "ACCOUNT_CONSOLIDATING")]
    [InlineData("CONTEXT_REVOKED", "CONTEXT_REVOKED")]
    [InlineData("CHARACTER_NOT_FOUND", "CHARACTER_NOT_FOUND")]
    [InlineData("WORLD_UNAVAILABLE", "WORLD_UNAVAILABLE")]
    [InlineData("SOMETHING_NEW", "SERVICE_UNAVAILABLE")]
    public async Task Keep_the_reason_of_a_refused_redemption(string refusal, string expected)
    {
        using var handler = new RecordingHandler((_, _) => Json(JoinRedemptionReceipt.Failure(refusal), HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);
        WorldAdmissionResult result = await new GameAdmissionClient(http, Options(), TimeProvider.System).AdmitAsync(GameAuthCryptography.NewToken(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result.Lease); Assert.Equal(expected, result.Error); Assert.Single(handler.Paths);
    }
    [Fact]
    public async Task Reject_success_shaped_http_error_and_oversized_body_and_redirect()
    {
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid(); JoinRedemptionReceipt receipt = Receipt(connection, redemption);
        foreach (string? mode in new[] { "error", "large", "redirect" })
        {
            using var handler = new RecordingHandler((_, _) => mode switch
            {
                "error" => Json(receipt, HttpStatusCode.InternalServerError),
                "large" => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 16385)) },
                _ => new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://attacker.example/") } }
            });
            using var http = new HttpClient(handler);
            WorldAdmissionResult result = await new GameAdmissionClient(http, Options(), TimeProvider.System).AdmitAsync(GameAuthCryptography.NewToken(), connection, redemption, CancellationToken.None);
            Assert.Null(result.Lease); Assert.Equal("SERVICE_UNAVAILABLE", result.Error); Assert.Single(handler.Paths);
        }
    }
    [Fact]
    public async Task Reject_replaced_identity_during_activation()
    {
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid(); JoinRedemptionReceipt receipt = Receipt(connection, redemption);
        using var handler = new RecordingHandler((call, _) => call == 1 ? Json(receipt) : Json(Active(receipt) with { AccountId = "43" }));
        using var http = new HttpClient(handler);
        WorldAdmissionResult result = await new GameAdmissionClient(http, Options(), TimeProvider.System).AdmitAsync(GameAuthCryptography.NewToken(), connection, redemption, CancellationToken.None);
        Assert.Null(result.Lease); Assert.Equal("INVALID_ADMISSION", result.Error);
    }
    /// <summary>
    /// #1013: an admission that fails after the redemption ends the session the receipt reserved before the world
    /// replies, so the player's next join is not refused while that session's lease runs out: whether the activation
    /// was refused, its transport failed (and the end's hangs too: the reply waits only a bounded time for it), or its
    /// answer could not be trusted. The end names exactly the receipt's account, session and fencing token. A defect that
    /// throws after the redemption ends the session too, then faults the admission, which hands back no lease (#1050).
    /// </summary>
    [Theory]
    [InlineData("refused")]
    [InlineData("transport")]
    [InlineData("untrusted")]
    [InlineData("defect")]
    public async Task End_the_reserved_session_when_admission_fails_after_the_redemption(string failure)
    {
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid(); JoinRedemptionReceipt receipt = Receipt(connection, redemption);
        // Every bound of the client runs on this clock, which moves only when the test moves it (#1019). A refused
        // activation is retried after a delay on the same clock, so that case runs on the system's.
        var fake = new FakeTimeProvider(DateTimeOffset.UtcNow);
        TimeProvider clock = failure == "refused" ? TimeProvider.System : fake;
        var ending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new RecordingHandler(async (call, request, cancellationToken) =>
        {
            if (call == 1) return Json(receipt);
            if (request.RequestUri!.AbsolutePath.EndsWith("/end", StringComparison.Ordinal))
            {
                if (failure != "transport") return Json(new SessionLeaseResponse { State = "ended" });
                ending.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return failure switch
            {
                "refused" => Json(new SessionLeaseResponse { Error = "BARRIER_PENDING" }, HttpStatusCode.ServiceUnavailable),
                "transport" => throw new HttpRequestException("connection reset"),
                "defect" => throw new InvalidOperationException("a defect"),
                _ => Json(Active(receipt) with { FencingToken = "8" })
            };
        });
        using var http = new HttpClient(handler);

        Task<WorldAdmissionResult> admitted = new GameAdmissionClient(http, Options(), clock).AdmitAsync(GameAuthCryptography.NewToken(), connection, redemption, CancellationToken.None);
        if (failure == "transport")
        {
            await ending.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(admitted.IsCompleted);
            // Well inside the five-second transport timeout: the end's own bound, not that, lets the reply go. Neither
            // the transport timeout nor the admission's runs out on this clock, so a reply that waited for one never comes.
            fake.Advance(TimeSpan.FromSeconds(2));
        }
        // A limit on a hang only: the reply waits for no wall-clock time but the refused case's two retry delays.
        if (failure == "defect")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => admitted.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        else
        {
            WorldAdmissionResult result = await admitted.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Null(result.Lease);
            Assert.Equal(failure switch { "refused" => "BARRIER_PENDING", "transport" => "SERVICE_UNAVAILABLE", _ => "INVALID_ADMISSION" }, result.Error);
        }
        Assert.Equal("/internal/game/sessions/end", handler.Paths[^1]);
        Assert.Single(handler.Paths, path => path.EndsWith("/end", StringComparison.Ordinal));
        using var end = System.Text.Json.JsonDocument.Parse(handler.Bodies[^1]);
        Assert.Equal(receipt.AccountId, end.RootElement.GetProperty("accountId").GetString());
        Assert.Equal(receipt.GameSessionId, end.RootElement.GetProperty("gameSessionId").GetString());
        Assert.Equal(receipt.FencingToken, end.RootElement.GetProperty("fencingToken").GetString());
    }
    /// <summary>
    /// #967: an identity from before the session-end reason refuses the unknown member with 400. The world must still end
    /// the session, or the player's next join meets ACTIVE_GAME_SESSION until the lease runs out; and it remembers, so
    /// the next ends cost one request each, offering the reason again only after the probe interval.
    /// </summary>
    [Fact]
    public async Task End_the_session_without_the_reason_when_identity_refuses_it()
    {
        using var handler = new RecordingHandler((call, _) => call == 1
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { title = "One or more validation errors occurred." }) }
            : Json(new SessionLeaseResponse { State = "ended" }));
        using var http = new HttpClient(handler);
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = new GameAdmissionClient(http, Options(), clock);

        SessionLeaseResponse reply = await client.EndAsync(WorldAdmissionConnection.Lease(clock), GameSessionEndReasons.Drop, default);

        Assert.Equal("ended", reply.State);
        Assert.Equal(2, handler.Paths.Count);
        Assert.Contains("\"reason\":\"drop\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("reason", handler.Bodies[1], StringComparison.OrdinalIgnoreCase);

        await client.EndAsync(WorldAdmissionConnection.Lease(clock), GameSessionEndReasons.Drop, default); // latched: one request
        clock.Advance(TimeSpan.FromMinutes(5));
        await client.EndAsync(WorldAdmissionConnection.Lease(clock), GameSessionEndReasons.Drop, default); // the probe
        Assert.Equal(4, handler.Paths.Count);
        Assert.DoesNotContain("reason", handler.Bodies[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"reason\":\"drop\"", handler.Bodies[3], StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    private sealed class RecordingHandler(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public RecordingHandler(Func<int, HttpRequestMessage, HttpResponseMessage> reply) : this((call, request, _) => Task.FromResult(reply(call, request))) { }
        public List<string> Bodies { get; } = [];
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("internal.avalon.example", request.RequestUri!.Host);
            Assert.Equal("https", request.RequestUri.Scheme);
            Assert.Equal(HttpMethod.Post, request.Method);
            Paths.Add(request.RequestUri.AbsolutePath);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return await reply(Paths.Count, request, cancellationToken);
        }
    }
}
