using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalon.Infrastructure.GameAuth;
using Avalon.World.GameAuth;

namespace Avalon.Server.World.UnitTests.GameAuth;

public sealed class GameAdmissionClientShould
{
    private static GameAdmissionOptions Options() => new() { ApiUrl = "https://internal.avalon.example/", ServerId = "world-one", WorldId = 1, ClientCertificatePath = "mounted-workload.pfx", ApiCertificateSha256 = new string('A', 64) };
    [Fact]
    public void Refuse_admission_configuration_without_an_explicit_API_certificate_pin()
    {
        var options = Options();
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
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid(); var receipt = Receipt(connection, redemption);
        using var handler = new RecordingHandler((call, _) => call switch
        {
            1 => Json(JoinRedemptionReceipt.Failure("IN_PROGRESS"), HttpStatusCode.Conflict),
            2 => Json(receipt),
            _ => Json(Active(receipt))
        });
        using var http = new HttpClient(handler);
        var client = new GameAdmissionClient(http, Options(), TimeProvider.System);
        var result = await client.AdmitAsync(GameAuthCryptography.NewToken(), connection, redemption, CancellationToken.None);
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
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid(); var receipt = Receipt(connection, redemption);
        receipt = mismatch switch
        {
            "server" => receipt with { ServerId = "world-two" },
            "connection" => receipt with { ConnectionId = Guid.NewGuid().ToString("D") },
            "redemption" => receipt with { RedemptionId = Guid.NewGuid().ToString("D") },
            _ => receipt with { WorldId = 2 }
        };
        using var handler = new RecordingHandler((_, _) => Json(receipt));
        using var http = new HttpClient(handler);
        var result = await new GameAdmissionClient(http, Options(), TimeProvider.System).AdmitAsync(GameAuthCryptography.NewToken(), connection, redemption, CancellationToken.None);
        Assert.Null(result.Lease); Assert.Equal("INVALID_ADMISSION", result.Error);
        Assert.Single(handler.Paths);
    }
    [Fact]
    public async Task Reject_success_shaped_http_error_and_oversized_body_and_redirect()
    {
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid(); var receipt = Receipt(connection, redemption);
        foreach (var mode in new[] { "error", "large", "redirect" })
        {
            using var handler = new RecordingHandler((_, _) => mode switch
            {
                "error" => Json(receipt, HttpStatusCode.InternalServerError),
                "large" => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 16385)) },
                _ => new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://attacker.example/") } }
            });
            using var http = new HttpClient(handler);
            var result = await new GameAdmissionClient(http, Options(), TimeProvider.System).AdmitAsync(GameAuthCryptography.NewToken(), connection, redemption, CancellationToken.None);
            Assert.Null(result.Lease); Assert.Equal("SERVICE_UNAVAILABLE", result.Error); Assert.Single(handler.Paths);
        }
    }
    [Fact]
    public async Task Reject_replaced_identity_during_activation()
    {
        var connection = Guid.NewGuid(); var redemption = Guid.NewGuid(); var receipt = Receipt(connection, redemption);
        using var handler = new RecordingHandler((call, _) => call == 1 ? Json(receipt) : Json(Active(receipt) with { AccountId = "43" }));
        using var http = new HttpClient(handler);
        var result = await new GameAdmissionClient(http, Options(), TimeProvider.System).AdmitAsync(GameAuthCryptography.NewToken(), connection, redemption, CancellationToken.None);
        Assert.Null(result.Lease); Assert.Equal("INVALID_ADMISSION", result.Error);
    }
    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    private sealed class RecordingHandler(Func<int, HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string> Paths { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("internal.avalon.example", request.RequestUri!.Host);
            Assert.Equal("https", request.RequestUri.Scheme);
            Assert.Equal(HttpMethod.Post, request.Method);
            Paths.Add(request.RequestUri.AbsolutePath);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return reply(Paths.Count, request);
        }
    }
}
