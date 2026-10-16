using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.Extensions.Logging;

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
    public bool IsValid() => Uri.TryCreate(ApiUrl, UriKind.Absolute, out Uri? uri) && uri.Scheme == "https" &&
        uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0 &&
        ServerId is { Length: >= 1 and <= 128 } && ServerId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') &&
        WorldId > 0 && !string.IsNullOrWhiteSpace(ClientCertificatePath) && ApiCertificateTrust.IsValidPin(ApiCertificateSha256);
}

public sealed record WorldAdmissionResult(GameSessionLease? Lease, string? Error);
public interface IGameAdmissionClient
{
    Task<WorldAdmissionResult> AdmitAsync(string ticket, Guid connectionId, Guid redemptionId, CancellationToken cancellationToken);
    Task<SessionLeaseResponse> HeartbeatAsync(GameSessionLease lease, CancellationToken cancellationToken);

    /// <summary>Ends the session, telling identity why (<see cref="GameSessionEndReasons"/>, #967).</summary>
    Task<SessionLeaseResponse> EndAsync(GameSessionLease lease, string reason, CancellationToken cancellationToken);
}

/// <summary>Bounded, fixed-origin workload requests. The transport supplies mTLS; credentials never enter logs.</summary>
public sealed class GameAdmissionClient(HttpClient http, GameAdmissionOptions options, TimeProvider clock,
    ILogger<GameAdmissionClient>? logger = null) : IGameAdmissionClient
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private Uri Endpoint(string path) => new(new Uri(options.ApiUrl), "internal/game/" + path);
    public async Task<WorldAdmissionResult> AdmitAsync(string ticket, Guid connectionId, Guid redemptionId, CancellationToken cancellationToken)
    {
        if (!options.IsValid() || !GameAuthCryptography.IsToken(ticket) || connectionId == Guid.Empty || redemptionId == Guid.Empty)
            return new(null, GameAuthErrors.InvalidAdmission);
        // Every bound here runs on the injected clock, the abandon's too, so each is measured against the others on one.
        using var admissionTimeout = new CancellationTokenSource(GameAuthPolicy.AdmissionTimeout, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, admissionTimeout.Token);
        // The session the redemption reserved, once its receipt checks out: ended again if no lease comes of it.
        JoinRedemptionReceipt? reserved = null;
        try
        {
            JoinRedemptionReceipt? receipt = null;
            for (int attempt = 0; attempt < GameAuthPolicy.MutationAttempts; attempt++)
            {
                receipt = await PostAsync<JoinRedemptionReceipt>("join-tickets/redeem", new { JoinTicket = ticket, ConnectionId = connectionId, RedemptionId = redemptionId }, deadline.Token);
                if (receipt?.Error is not (GameAuthErrors.InProgress or GameAuthErrors.ServiceUnavailable)) break;
                await Task.Delay(GameAuthPolicy.RetryDelay, clock, deadline.Token);
            }
            if (receipt is null || receipt.Error is not null) return new(null, SafeError(receipt?.Error));
            if (receipt.State != GameAuthStates.Pending || receipt.ServerId != options.ServerId || receipt.WorldId != options.WorldId ||
                receipt.ConnectionId != connectionId.ToString("D") || receipt.RedemptionId != redemptionId.ToString("D") ||
                !Positive(receipt.AccountId) || !Positive(receipt.FencingToken) ||
                !Guid.TryParseExact(receipt.GameSessionId, "D", out Guid session) || session == Guid.Empty ||
                !Guid.TryParseExact(receipt.GameContextId, "D", out Guid context) || context == Guid.Empty)
            {
                return new(null, GameAuthErrors.InvalidAdmission);
            }

            reserved = receipt;
            SessionLeaseResponse? response = null;
            for (int attempt = 0; attempt < GameAuthPolicy.MutationAttempts; attempt++)
            {
                response = await PostAsync<SessionLeaseResponse>("sessions/activate", new { receipt.AccountId, GameSessionId = session, receipt.FencingToken }, deadline.Token);
                if (response?.Error is not (GameAuthErrors.BarrierPending or GameAuthErrors.ServiceUnavailable)) break;
                await Task.Delay(GameAuthPolicy.RetryDelay, clock, deadline.Token);
            }
            if (response is null || response.Error is not null) return await AbandonAsync(receipt, SafeError(response?.Error));
            if (response.AccountId != receipt.AccountId || response.GameSessionId != receipt.GameSessionId || response.GameContextId != receipt.GameContextId ||
                response.FencingToken != receipt.FencingToken || response.SessionEpoch != receipt.SessionEpoch || response.CredentialsVersion != receipt.CredentialsVersion)
            {
                return await AbandonAsync(receipt, GameAuthErrors.InvalidAdmission);
            }

            var lease = GameSessionLease.TryCreate(response, options.ServerId, options.WorldId, clock);
            return lease is null ? await AbandonAsync(receipt, GameAuthErrors.InvalidAdmission) : new(lease, null);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException)
        { return await AbandonAsync(reserved, GameAuthErrors.ServiceUnavailable); }
        catch (Exception) when (reserved is not null)
        {
            // A defect after the redemption reserved a session (#1050): a faulted admission hands back no lease, so the
            // world could never end the session itself. End it, then let the fault through, which the admission
            // reports and answers ServiceUnavailable. A second fault in that end is dropped: the first is the one to
            // report, and the session still runs out with its lease.
            try { await AbandonAsync(reserved, GameAuthErrors.ServiceUnavailable); }
            catch (Exception) { }
            throw;
        }
    }

    /// <summary>
    /// How long a failed admission waits to end the session its redemption reserved, before the world replies. The
    /// end is one internal request; past this the reply goes out, and the session runs out with its lease.
    /// </summary>
    private static readonly TimeSpan s_abandonTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// A failed admission after the redemption (#1013): ends the session the receipt reserved, best-effort and bounded,
    /// so the player's next join is not refused while that session's lease runs out. The API ends only the session
    /// that still holds this receipt's id and fencing token on this world's workload identity; a pending one simply
    /// runs out, and an active one (its activation answered, but the answer was lost or refused here) ends as at a
    /// logout. On its own timer, not the admission's: a deadline that ran out must not cancel the cleanup.
    /// </summary>
    private async Task<WorldAdmissionResult> AbandonAsync(JoinRedemptionReceipt? receipt, string error)
    {
        if (receipt is not null && Guid.TryParseExact(receipt.GameSessionId, "D", out Guid session))
        {
            using var bound = new CancellationTokenSource(s_abandonTimeout, clock);
            await SessionControlAsync("end", receipt.AccountId!, session, receipt.FencingToken!, bound.Token);
        }
        return new(null, error);
    }
    public Task<SessionLeaseResponse> HeartbeatAsync(GameSessionLease lease, CancellationToken cancellationToken) =>
        ControlAsync("heartbeat", lease, null, cancellationToken);

    public async Task<SessionLeaseResponse> EndAsync(GameSessionLease lease, string reason, CancellationToken cancellationToken)
    {
        long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        if (_identityRefusesReason)
        {
            // Latched: ends go without the reason, one request each, but one in every probe interval, which asks again.
            long due = Interlocked.Read(ref _reasonProbeAt);
            if (now < due || Interlocked.CompareExchange(ref _reasonProbeAt, now + ReasonProbeInterval, due) != due)
                return await ControlAsync("end", lease, null, cancellationToken);
        }

        SessionLeaseResponse reply = await ControlAsync("end", lease, reason, cancellationToken);
        if (reply.Error != RefusedRequest)
        {
            if (reply.Error is null) _identityRefusesReason = false; // identity takes the reason now
            return reply;
        }

        // An identity from before #967 refuses the member it does not know (400): end the session anyway, without the
        // reason, or the player's next join would meet ACTIVE_GAME_SESSION until the lease ran out. Remembered, so a
        // stop's ends do not each cost two requests while that identity runs.
        Interlocked.Exchange(ref _reasonProbeAt, now + ReasonProbeInterval);
        if (!_identityRefusesReason)
        {
            _identityRefusesReason = true;
            logger?.LogWarning(
                "Identity refused the reason of a game session end: it predates #967. Session ends are sent without it, and the reason is offered again every {Minutes} minutes",
                ReasonProbeInterval / 60_000);
        }

        return await ControlAsync("end", lease, null, cancellationToken);
    }

    /// <summary>How often, in ms, an end offers its reason again once identity refused it.</summary>
    private const long ReasonProbeInterval = 5 * 60_000;

    // Whether identity refused an end's reason (#967): ends then go without it until the next probe (_reasonProbeAt,
    // Unix ms on the injected clock) finds an identity that takes it.
    private volatile bool _identityRefusesReason;
    private long _reasonProbeAt;

    /// <summary>A 400 with no error of identity's own to a request that carried a reason: the request's shape was refused.</summary>
    private const string RefusedRequest = "REQUEST_REFUSED";

    private Task<SessionLeaseResponse> ControlAsync(string action, GameSessionLease lease, string? reason,
        CancellationToken cancellationToken) =>
        SessionControlAsync(action, lease.Authority.AccountId.Value.ToString(CultureInfo.InvariantCulture), lease.Authority.GameSessionId,
            lease.Authority.FencingToken.ToString(CultureInfo.InvariantCulture), cancellationToken, reason);

    private async Task<SessionLeaseResponse> SessionControlAsync(string action, string accountId, Guid gameSessionId, string fencingToken,
        CancellationToken cancellationToken, string? reason = null)
    {
        object body = reason is null
            ? new { AccountId = accountId, GameSessionId = gameSessionId, FencingToken = fencingToken }
            : new { AccountId = accountId, GameSessionId = gameSessionId, FencingToken = fencingToken, Reason = reason };
        try
        {
            (HttpStatusCode status, SessionLeaseResponse? reply) = await PostWithStatusAsync<SessionLeaseResponse>(
                "sessions/" + action, body, cancellationToken);
            // Only a request that carried a reason is told apart: every other refusal reads as it always has.
            if (reason is not null && status == HttpStatusCode.BadRequest && reply?.Error is null) return new() { Error = RefusedRequest };
            return reply ?? new() { Error = GameAuthErrors.ServiceUnavailable };
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException)
        { return new() { Error = GameAuthErrors.ServiceUnavailable }; }
    }

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken cancellationToken) =>
        (await PostWithStatusAsync<T>(path, body, cancellationToken)).Result;

    private async Task<(HttpStatusCode Status, T? Result)> PostWithStatusAsync<T>(string path, object body,
        CancellationToken cancellationToken)
    {
        using var transportTimeout = new CancellationTokenSource(GameAuthPolicy.TransportTimeout, clock);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, transportTimeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(path)) { Content = JsonContent.Create(body) };
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        HttpStatusCode status = response.StatusCode;
        if (status is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            return (status, default);
        if (response.Content.Headers.ContentLength is > GameAuthPolicy.MaximumBodyBytes) return (status, default);
        await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (bytes.Length + read > GameAuthPolicy.MaximumBodyBytes) return (status, default);
            bytes.Write(buffer, 0, read);
        }
        if (bytes.Length == 0) return (status, default);
        T? result = JsonSerializer.Deserialize<T>(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), s_json);
        // A success-shaped error response must never become authority.
        if (!response.IsSuccessStatusCode)
        {
            if (result is SessionLeaseResponse lease && lease.Error is null || result is JoinRedemptionReceipt redemption && redemption.Error is null)
                return (status, default);
        }
        return (status, result);
    }
    private static bool Positive(string? text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value > 0 && value.ToString(CultureInfo.InvariantCulture) == text;
    /// <summary>
    /// The identity service's refusal as the admission handler maps it, when it is one the world knows; any other is
    /// "try later". The permanent ones a redemption names (#1053: the client too old, the context not allowed on the
    /// world, revoked, its account's standing, the character not the account's) pass through, so the client is not
    /// told to retry them.
    /// </summary>
    private static string SafeError(string? error) => error switch
    {
        GameAuthErrors.SessionRevoked or GameAuthErrors.SessionConflict or GameAuthErrors.SessionReplaced or GameAuthErrors.InvalidTicket or
            GameAuthErrors.WorldUnavailable or GameAuthErrors.WorldNotAllowed or GameAuthErrors.BarrierPending or
            GameAuthErrors.ClientOutdated or GameAuthErrors.UnsupportedProtocol or GameAuthErrors.ContextRevoked or
            GameAuthErrors.AccountBanned or GameAuthErrors.AccountDeactivated or GameAuthErrors.AccountConsolidating or
            GameAuthErrors.CharacterNotFound => error,
        _ => GameAuthErrors.ServiceUnavailable
    };
}
