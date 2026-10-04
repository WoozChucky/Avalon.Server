using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;

namespace Avalon.World.GameAuth;

public sealed class GameAdmissionOptions
{
    public const string Section = "World:Admission";
    public string ApiUrl { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public ushort WorldId { get; set; }
    public string ClientCertificatePath { get; set; } = string.Empty;
    public string? ClientCertificatePassword { get; set; }
    public string ApiCertificateSha256 { get; set; } = string.Empty;
    public bool IsValid() => Uri.TryCreate(ApiUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0 &&
        ServerId is { Length: >= 1 and <= 128 } && ServerId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
        WorldId > 0 && !string.IsNullOrWhiteSpace(ClientCertificatePath) && ApiCertificateTrust.IsValidPin(ApiCertificateSha256);
}

public sealed record WorldAdmissionResult(GameSessionLease? Lease, string? Error);
public interface IGameAdmissionClient
{
    Task<WorldAdmissionResult> AdmitAsync(string ticket, Guid connectionId, Guid redemptionId, CancellationToken cancellationToken);
    Task<SessionLeaseResponse> HeartbeatAsync(GameSessionLease lease, CancellationToken cancellationToken);
    Task<SessionLeaseResponse> EndAsync(GameSessionLease lease, CancellationToken cancellationToken);
}

/// <summary>Bounded, fixed-origin workload requests. The transport supplies mTLS; credentials never enter logs.</summary>
public sealed class GameAdmissionClient(HttpClient http, GameAdmissionOptions options, TimeProvider clock) : IGameAdmissionClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Uri Endpoint(string path) => new(new Uri(options.ApiUrl), "internal/game/" + path);
    public async Task<WorldAdmissionResult> AdmitAsync(string ticket, Guid connectionId, Guid redemptionId, CancellationToken cancellationToken)
    {
        if (!options.IsValid() || !GameAuthCryptography.IsToken(ticket) || connectionId == Guid.Empty || redemptionId == Guid.Empty)
            return new(null, GameAuthErrors.InvalidAdmission);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(GameAuthPolicy.AdmissionTimeout);
        try
        {
            JoinRedemptionReceipt? receipt = null;
            for (int attempt = 0; attempt < GameAuthPolicy.MutationAttempts; attempt++)
            {
                receipt = await PostAsync<JoinRedemptionReceipt>("join-tickets/redeem", new { JoinTicket = ticket, ConnectionId = connectionId, RedemptionId = redemptionId }, deadline.Token);
                if (receipt?.Error is not (GameAuthErrors.InProgress or GameAuthErrors.ServiceUnavailable)) break;
                await Task.Delay(GameAuthPolicy.RetryDelay, deadline.Token);
            }
            if (receipt is null || receipt.Error is not null) return new(null, SafeError(receipt?.Error));
            if (receipt.State != GameAuthStates.Pending || receipt.ServerId != options.ServerId || receipt.WorldId != options.WorldId ||
                receipt.ConnectionId != connectionId.ToString("D") || receipt.RedemptionId != redemptionId.ToString("D") ||
                !Positive(receipt.AccountId) || !Positive(receipt.FencingToken) ||
                !Guid.TryParseExact(receipt.GameSessionId, "D", out var session) || session == Guid.Empty ||
                !Guid.TryParseExact(receipt.GameContextId, "D", out var context) || context == Guid.Empty)
                return new(null, GameAuthErrors.InvalidAdmission);
            SessionLeaseResponse? response = null;
            for (int attempt = 0; attempt < GameAuthPolicy.MutationAttempts; attempt++)
            {
                response = await PostAsync<SessionLeaseResponse>("sessions/activate", new { receipt.AccountId, GameSessionId = session, receipt.FencingToken }, deadline.Token);
                if (response?.Error is not (GameAuthErrors.BarrierPending or GameAuthErrors.ServiceUnavailable)) break;
                await Task.Delay(GameAuthPolicy.RetryDelay, deadline.Token);
            }
            if (response is null || response.Error is not null) return new(null, SafeError(response?.Error));
            if (response.AccountId != receipt.AccountId || response.GameSessionId != receipt.GameSessionId || response.GameContextId != receipt.GameContextId ||
                response.FencingToken != receipt.FencingToken || response.SessionEpoch != receipt.SessionEpoch || response.CredentialsVersion != receipt.CredentialsVersion)
                return new(null, GameAuthErrors.InvalidAdmission);
            var lease = GameSessionLease.TryCreate(response, options.ServerId, options.WorldId, clock);
            return new(lease, lease is null ? GameAuthErrors.InvalidAdmission : null);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException)
        { return new(null, GameAuthErrors.ServiceUnavailable); }
    }
    public Task<SessionLeaseResponse> HeartbeatAsync(GameSessionLease lease, CancellationToken cancellationToken) => ControlAsync("heartbeat", lease, cancellationToken);
    public Task<SessionLeaseResponse> EndAsync(GameSessionLease lease, CancellationToken cancellationToken) => ControlAsync("end", lease, cancellationToken);
    private async Task<SessionLeaseResponse> ControlAsync(string action, GameSessionLease lease, CancellationToken cancellationToken)
    {
        try
        {
            return await PostAsync<SessionLeaseResponse>("sessions/" + action, new
            {
                AccountId = lease.Authority.AccountId.Value.ToString(CultureInfo.InvariantCulture),
                lease.Authority.GameSessionId, FencingToken = lease.Authority.FencingToken.ToString(CultureInfo.InvariantCulture)
            }, cancellationToken) ?? new() { Error = GameAuthErrors.ServiceUnavailable };
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException)
        { return new() { Error = GameAuthErrors.ServiceUnavailable }; }
    }
    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GameAuthPolicy.TransportTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(path)) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode is System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.MovedPermanently or System.Net.HttpStatusCode.TemporaryRedirect or System.Net.HttpStatusCode.PermanentRedirect)
            return default;
        if (response.Content.Headers.ContentLength is > GameAuthPolicy.MaximumBodyBytes) return default;
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (bytes.Length + read > GameAuthPolicy.MaximumBodyBytes) return default;
            bytes.Write(buffer, 0, read);
        }
        if (bytes.Length == 0) return default;
        var result = JsonSerializer.Deserialize<T>(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), Json);
        // A success-shaped error response must never become authority.
        if (!response.IsSuccessStatusCode)
        {
            if (result is SessionLeaseResponse lease && lease.Error is null || result is JoinRedemptionReceipt redemption && redemption.Error is null)
                return default;
        }
        return result;
    }
    private static bool Positive(string? text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0 && value.ToString(CultureInfo.InvariantCulture) == text;
    private static string SafeError(string? error) => error switch
    {
        GameAuthErrors.SessionRevoked or GameAuthErrors.SessionConflict or GameAuthErrors.SessionReplaced or GameAuthErrors.InvalidTicket or GameAuthErrors.WorldUnavailable or GameAuthErrors.BarrierPending => error,
        _ => GameAuthErrors.ServiceUnavailable
    };
}
