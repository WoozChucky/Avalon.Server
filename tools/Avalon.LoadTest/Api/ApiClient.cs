using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalon.Common.GameAuth;

namespace Avalon.LoadTest.Api;

/// <summary>
/// The REST calls a bot and the run commands make: the launcher's sign-in chain to a game context (as
/// <c>tools/Avalon.LocalDev</c> walks it), join tickets, context refresh and logout, and the admin load-test account
/// endpoints. Writes nothing to the console and never logs out a context it hands back: a bot's context must outlive
/// each call (a sign-in signs out only a context it made but does not hand back). Every failure is an <see cref="ApiException"/> naming its step; a cancellation stays an
/// <see cref="OperationCanceledException"/>. Thread-safe: one client serves every bot.
/// </summary>
public sealed class ApiClient(Uri api, TimeSpan timeout) : IDisposable
{
    /// <summary>The protocol the provider attempt must name: <c>GameWorkloadConfiguration.ClientProtocolVersion</c>.</summary>
    public const string ProtocolVersion = "0.2.0";

    /// <summary>The application the provider attempt is for.</summary>
    public const string ApplicationKey = "avalon.base";

    private const string DeviceName = "Avalon.LoadTest";

    /// <summary>The launcher's loopback port; nothing listens, the code comes back in the body.</summary>
    private const int RedirectPort = 49152;

    /// <summary>The waits before the retries of a repeatable call (<see cref="SendWithRetriesAsync"/>).</summary>
    private static readonly TimeSpan[] s_retryDelays = [TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1)];

    /// <summary>A refresh attempt's own timeout: three attempts and their waits stay inside the server's 30 s receipt.</summary>
    private static readonly TimeSpan s_refreshAttemptTimeout = TimeSpan.FromSeconds(8);

    /// <summary>A redeem attempt's own timeout.</summary>
    private static readonly TimeSpan s_redeemAttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long the server holds a redeem's claim (<c>GameAuthPolicy.MutationClaimLifetime</c>): while it does, a retry
    /// of the key is answered 409 <c>IN_PROGRESS</c>, and the attempt holding it may still commit the context.
    /// </summary>
    private static readonly TimeSpan s_redeemClaim = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A redeem is asked again until this long after its first send: the claim, then one whole attempt after it, and a
    /// margin (30 s). By then the first attempt has committed (a retry gets its receipt) or lost its claim (a retry
    /// redeems afresh).
    /// </summary>
    private static readonly TimeSpan s_redeemBudget = s_redeemClaim + s_redeemAttemptTimeout + TimeSpan.FromSeconds(5);

    /// <summary>The wait between a redeem's attempts.</summary>
    private static readonly TimeSpan s_redeemRetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>The shortest timeout a redeem attempt gets, however little of the budget is left.</summary>
    private static readonly TimeSpan s_redeemMinAttempt = TimeSpan.FromSeconds(2);

    /// <summary>The shortest timeout a logout attempt gets, and the least time left worth a retry.</summary>
    private static readonly TimeSpan s_logoutMinAttempt = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a sign-out (<see cref="LogoutAsync"/>) is given, by every caller, on its own clock. Its first attempt
    /// may use all of it; a quick failure (no connection, a 5xx) is retried in what is left.
    /// </summary>
    public static TimeSpan LogoutTimeout { get; } = TimeSpan.FromSeconds(10);

    // No redirect is followed: a 307 or 308 would send a body holding a password or a credential on to another place.
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = false,
    })
    {
        BaseAddress = Origin(api),
        Timeout = timeout,
    };

    /// <summary>
    /// Signs <paramref name="username"/> in as the launcher and the game client do: authenticate, a PKCE launcher code
    /// with the password again, the launcher token, a game ticket, a provider attempt and the handoff redemption. Two
    /// password checks (BCrypt) on the API per call: sign a bot in once and refresh its context after. The redeem, which
    /// creates the context, is seen through once sent, <paramref name="ct"/> cancelled or not: a reply dropped there would
    /// leave a context live for 5 minutes that nobody can sign out. A context it made but does not hand back (not
    /// authorized, or a reply lacking what a bot needs) is signed out before the failure is thrown.
    /// </summary>
    public async Task<GameContext> SignInAsync(string username, string password, CancellationToken ct)
    {
        string accessToken = await AuthenticateAsync(username, password, ct);

        string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        JsonNode code = await SendAsync("code", HttpMethod.Post, "client/auth/code",
            new { challenge, redirectPort = RedirectPort, currentPassword = password }, accessToken, idempotencyKey: null, ct);
        JsonNode launcher = await SendAsync("token", HttpMethod.Post, "client/auth/token",
            new { code = Required(code, "code", "code"), verifier, redirectPort = RedirectPort, deviceName = DeviceName },
            null, idempotencyKey: null, ct);
        JsonNode ticket = await SendAsync("game-ticket", HttpMethod.Post, "client/auth/game-ticket", new { },
            Required(launcher, "accessToken", "token"), idempotencyKey: null, ct);

        JsonNode attempt = await SendAsync("provider-attempt", HttpMethod.Post, "client/auth/provider-attempts", new
        {
            applicationKey = ApplicationKey,
            protocolVersion = ProtocolVersion,
            clientRunId = Guid.NewGuid(),
            linkChallenge = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
        }, null, idempotencyKey: null, ct);
        var redeem = new
        {
            attemptCredential = Required(attempt, "attemptCredential", "provider-attempt"),
            handoffTicket = Required(ticket, "ticket", "game-ticket"),
        };

        // Not sent once cancelled; once sent, not cancelled. No reply in time, a transport failure, a 5xx or a 409
        // IN_PROGRESS (an earlier attempt still holds the server's claim) is asked again under the same key until 30 s
        // from the first send: past the claim, the server answers with what the first attempt did, or redeems afresh.
        ct.ThrowIfCancellationRequested();
        JsonNode redeemed = await SendUntilAsync("redeem", "client/auth/handoffs/redeem", Guid.NewGuid(), () => redeem,
            GameAuthErrors.InProgress, s_redeemAttemptTimeout, s_redeemBudget);

        string? made = Text(redeemed, "gameContextCredential");
        try
        {
            RequireAuthorized(redeemed, "redeem");
            return new GameContext(
                Required(redeemed, "gameContextCredential", "redeem"),
                Required(redeemed, "gameContextRefreshToken", "redeem"),
                RequiredTime(redeemed, "contextExpiresAt", "redeem"),
                RequiredTime(redeemed, "authorizationValidUntil", "redeem"));
        }
        catch (ApiException) when (made is not null)
        {
            // A context made but not handed back (not authorized, or the reply lacks what a bot needs) is signed out.
            using var limit = new CancellationTokenSource(LogoutTimeout);
            try
            {
                await LogoutCredentialAsync(made, limit.Token);
            }
            catch (Exception error) when (error is ApiException || (error is OperationCanceledException && limit.IsCancellationRequested))
            {
                // Best effort: the failure rethrown is the caller's; the context expires within 5 minutes.
            }

            throw;
        }
    }

    /// <summary>
    /// A join ticket for world <paramref name="worldId"/>. <paramref name="confirmTakeover"/> replaces the account's
    /// live world session, which is otherwise refused (409, <c>ACTIVE_GAME_SESSION</c>). A reply naming another world is
    /// refused here: the tool enters only the world it was told to. A timeout, transport failure or 5xx is retried twice
    /// under the same <c>Idempotency-Key</c>, and so is a 409 <c>CONTEXT_CHANGED</c> (the context moved on while the
    /// ticket was issued, a refresh of it most likely; nothing was kept for the key), with the context's credential
    /// read again for the retry.
    /// </summary>
    public async Task<JoinTicket> JoinTicketAsync(GameContext context, ushort worldId, bool confirmTakeover,
        CancellationToken ct)
    {
        const string Step = "join";
        // The key is kept across the retries. A retry meeting a receipt the server already stored for it, issued while
        // the context was at an earlier generation (a refresh rotated it in between), is answered 409
        // IDEMPOTENCY_CONFLICT, which is not retried here: the attempt fails, and Bot.EnterAsync's next attempt asks
        // again under a new key.
        JsonNode reply = await SendWithRetriesAsync(Step, "game/join-tickets", Guid.NewGuid(),
            () => new { gameContextCredential = context.Credential, worldId, confirmTakeover }, GameAuthErrors.ContextChanged,
            attemptTimeout: null, ct);
        string ticket = Required(reply, "joinTicket", Step);
        JsonNode destination = reply["destination"] ?? throw new ApiException(Step, 200, "the reply has no destination");

        WorldDestination parsed;
        try
        {
            parsed = new WorldDestination(
                destination["worldId"]?.GetValue<ushort>() ?? throw new ApiException(Step, 200, "the destination has no worldId"),
                Required(destination, "host", Step),
                destination["port"]?.GetValue<int>() ?? throw new ApiException(Step, 200, "the destination has no port"),
                Required(destination, "tlsServerName", Step),
                Required(destination, "tlsCertificateSha256", Step));
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            throw new ApiException(Step, 200, $"the destination is malformed: {error.Message}");
        }

        if (parsed.WorldId != worldId)
            throw new ApiException(Step, 200, $"the ticket is for world {parsed.WorldId}, not world {worldId}");

        return new JoinTicket(ticket, parsed);
    }

    /// <summary>
    /// Refreshes the context: both the credential and the refresh token rotate, and the expiries move on. Sent under
    /// the generation's <see cref="GameContextTokens.RefreshKey"/>, the same on every retry and every later call until
    /// the token rotates. Each attempt has 8 s (<see cref="s_refreshAttemptTimeout"/>), so the retries land within the
    /// 30 s the server keeps a refresh's receipt; a timeout, transport failure or 5xx is retried twice, and so is a 409
    /// <c>IN_PROGRESS</c> (the server lost the race to rotate the token; a retry of the same key either rotates it or
    /// is answered with what an earlier attempt of the key did). A 401 means the context cannot be refreshed again
    /// (<c>CONTEXT_REVOKED</c>, <c>INVALID_REFRESH</c>, <c>REFRESH_REUSE</c>): sign in afresh. So does a reply that is
    /// not authorized (<see cref="ApiException.State"/> set): the context is no longer one the world admits. The server
    /// rotated it all the same, so a reply's credential is kept in <paramref name="context"/> before any failure is
    /// thrown: a sign-out of the context then reaches it.
    /// </summary>
    public async Task RefreshAsync(GameContext context, CancellationToken ct)
    {
        const string Step = "refresh";
        GameContextTokens spent = context.Current;
        var body = new { gameContextRefreshToken = spent.RefreshToken };
        JsonNode reply = await SendWithRetriesAsync(Step, "client/auth/game-context/refresh", spent.RefreshKey, () => body,
            GameAuthErrors.InProgress, s_refreshAttemptTimeout, ct);
        // A credential in the reply is the one the server holds now, whatever else the reply says: kept before anything
        // is refused, so a sign-out of the context reaches it.
        if (Text(reply, "gameContextCredential") is { } credential)
        {
            context.Rotate(spent, credential, Text(reply, "gameContextRefreshToken") ?? spent.RefreshToken,
                Time(reply, "contextExpiresAt") ?? spent.ContextExpiresAt,
                Time(reply, "authorizationValidUntil") ?? spent.AuthorizationValidUntil);
        }

        RequireAuthorized(reply, Step);
        _ = Required(reply, "gameContextCredential", Step);
        _ = Required(reply, "gameContextRefreshToken", Step);
        _ = RequiredTime(reply, "contextExpiresAt", Step);
        _ = RequiredTime(reply, "authorizationValidUntil", Step);
    }

    /// <summary>
    /// Signs the game context out; a context already gone (401, 404) counts as signed out. Each attempt has what is left
    /// of <see cref="LogoutTimeout"/> from the first send (the server drops a logout whose request is aborted, so an
    /// attempt is never cut short for a retry's sake). A logout is safe to repeat (a second one finds the context
    /// revoked and changes nothing), so a transport failure or a 5xx is asked again, twice at most, while at least 1 s
    /// is left after the wait; a timeout has spent the time and is not. Give it <see cref="LogoutTimeout"/>.
    /// </summary>
    public Task LogoutAsync(GameContext context, CancellationToken ct) => LogoutCredentialAsync(context.Credential, ct);

    private async Task LogoutCredentialAsync(string credential, CancellationToken ct)
    {
        long start = Stopwatch.GetTimestamp();
        for (int attempt = 0; ; attempt++)
        {
            TimeSpan left = LogoutTimeout - Stopwatch.GetElapsedTime(start);
            try
            {
                await SendAsync("logout", HttpMethod.Post, "client/auth/game-context/logout",
                    new { gameContextCredential = credential }, null, idempotencyKey: null, ct, allowEmpty: true,
                    attemptTimeout: left > s_logoutMinAttempt ? left : s_logoutMinAttempt);
                return;
            }
            catch (ApiException error) when (error.Status is 401 or 404)
            {
                return;
            }
            catch (ApiException error) when (attempt < s_retryDelays.Length && error.Status is 0 or >= 500 &&
                LogoutTimeout - Stopwatch.GetElapsedTime(start) >= s_retryDelays[attempt] + s_logoutMinAttempt)
            {
                await Task.Delay(s_retryDelays[attempt], ct);
            }
        }
    }

    /// <summary>An admin's access token, from <c>account/authenticate</c>; an account with MFA on is refused.</summary>
    public Task<string> AdminTokenAsync(string username, string password, CancellationToken ct) =>
        AuthenticateAsync(username, password, ct);

    /// <summary>
    /// <c>POST admin/load-test/accounts</c>: a run of <paramref name="count"/> (1 to 1,000) bot accounts signing in
    /// with <paramref name="botPassword"/>. <paramref name="runId"/> is three letters, generated by the API when null;
    /// a run id already used is refused (409).
    /// </summary>
    public async Task<(string RunId, IReadOnlyList<string> Accounts)> CreateRunAsync(string adminToken, string? runId,
        int count, string botPassword, string currentPassword, CancellationToken ct)
    {
        const string Step = "create-run";
        JsonNode reply = await SendAsync(Step, HttpMethod.Post, "admin/load-test/accounts",
            new { runId, count, password = botPassword, currentPassword }, adminToken, idempotencyKey: null, ct);
        return (Required(reply, "runId", Step), Strings(reply, "accounts", Step));
    }

    /// <summary>
    /// <c>DELETE admin/load-test/accounts?run=</c>: the run's accounts with their characters in every world, and the
    /// usernames of those kept because they hold more than a load test gives. 409 while a bot holds a live session.
    /// </summary>
    public async Task<(int Deleted, IReadOnlyList<string> Skipped)> DeleteRunAsync(string adminToken, string runId,
        string currentPassword, CancellationToken ct)
    {
        const string Step = "delete-run";
        JsonNode reply = await SendAsync(Step, HttpMethod.Delete,
            $"admin/load-test/accounts?run={Uri.EscapeDataString(runId)}", new { currentPassword }, adminToken,
            idempotencyKey: null, ct);
        try
        {
            int deleted = reply["deleted"]?.GetValue<int>() ?? throw new ApiException(Step, 200, "the reply has no deleted");
            return (deleted, Strings(reply, "skipped", Step));
        }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            throw new ApiException(Step, 200, $"the reply is malformed: {error.Message}");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>
    /// The https origin with its trailing slash, which keeps a path prefix (an ingress's /api) when routes resolve. Any
    /// other scheme is refused: passwords and credentials never travel in the clear.
    /// </summary>
    private static Uri Origin(Uri api)
    {
        if (!api.IsAbsoluteUri || api.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException($"The API must be an https origin, not {api}.", nameof(api));

        return api.AbsolutePath.EndsWith('/') ? api : new Uri(api.AbsoluteUri + "/");
    }

    private async Task<string> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        const string Step = "authenticate";
        JsonNode reply = await SendAsync(Step, HttpMethod.Post, "account/authenticate", new { username, password }, null,
            idempotencyKey: null, ct);
        if (reply["token"] is JsonValue token && token.TryGetValue(out string? value) && value.Length > 0)
            return value;

        throw new ApiException(Step, 200, reply["mfaHash"] is not null
            ? $"{username} has MFA on, which this tool does not answer: use an account without it"
            : $"no token for {username} (status {reply["status"]}{WithError(reply)})");
    }

    /// <summary>
    /// A POST that is safe to repeat: every attempt carries the same <c>Idempotency-Key</c>, so a reply lost to a timeout
    /// or a transport failure (status 0), or a 5xx, is asked again and the server answers what it already did. That
    /// matters for a refresh: its refresh token is one-use, and a second key would find it already rotated. A 409 whose
    /// error is <paramref name="retryConflict"/> is asked again too: the call's own word that the request did not take
    /// and may be repeated. <paramref name="body"/> is built for each attempt.
    /// </summary>
    /// <param name="key">The <c>Idempotency-Key</c> of every attempt.</param>
    /// <param name="attemptTimeout">Each attempt's timeout when shorter than the client's; null for the client's.</param>
    private async Task<JsonNode> SendWithRetriesAsync(string step, string path, Guid key, Func<object> body,
        string retryConflict, TimeSpan? attemptTimeout, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await SendAsync(step, HttpMethod.Post, path, body(), null, key, ct, attemptTimeout: attemptTimeout);
            }
            catch (ApiException error) when (attempt < s_retryDelays.Length && Repeatable(error, retryConflict))
            {
                await Task.Delay(s_retryDelays[attempt], ct);
            }
        }
    }

    /// <summary>
    /// As <see cref="SendWithRetriesAsync"/>, never cancelled, asked again 1 s apart under the same key until
    /// <paramref name="budget"/> from the first send has run out: each attempt has what is left of the budget, held
    /// between 2 s and <paramref name="attemptTimeout"/>, and none starts once the budget is spent, so the whole call
    /// takes at most the budget and 2 s.
    /// </summary>
    private async Task<JsonNode> SendUntilAsync(string step, string path, Guid key, Func<object> body,
        string retryConflict, TimeSpan attemptTimeout, TimeSpan budget)
    {
        long start = Stopwatch.GetTimestamp();
        while (true)
        {
            // Read after any wait: a delay that ran late (a starved thread pool) leaves less, never a negative timeout.
            TimeSpan left = budget - Stopwatch.GetElapsedTime(start);
            TimeSpan timeout = left < s_redeemMinAttempt ? s_redeemMinAttempt : left > attemptTimeout ? attemptTimeout : left;
            try
            {
                return await SendAsync(step, HttpMethod.Post, path, body(), null, key, CancellationToken.None,
                    attemptTimeout: timeout);
            }
            catch (ApiException error) when (Repeatable(error, retryConflict) && Stopwatch.GetElapsedTime(start) < budget)
            {
                await Task.Delay(s_redeemRetryDelay);
                // The wait ended past the deadline: the last failure stands.
                if (Stopwatch.GetElapsedTime(start) >= budget) throw;
            }
        }
    }

    /// <summary>No reply (status 0), a 5xx, or a 409 whose error is <paramref name="retryConflict"/>.</summary>
    private static bool Repeatable(ApiException error, string retryConflict) =>
        error.Status is 0 or >= 500 || (error.Status == 409 && error.Detail == retryConflict);

    private async Task<JsonNode> SendAsync(string step, HttpMethod method, string path, object body, string? bearer,
        Guid? idempotencyKey, CancellationToken ct, bool allowEmpty = false, TimeSpan? attemptTimeout = null)
    {
        TimeSpan timeout = attemptTimeout is { } own && own < _http.Timeout ? own : _http.Timeout;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (idempotencyKey is { } key) request.Headers.Add("Idempotency-Key", key.ToString("D"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, limit.Token);
        }
        catch (HttpRequestException error)
        {
            throw new ApiException(step, 0, error.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(step, 0, $"no reply within {timeout.TotalSeconds:0} s");
        }

        using (response)
        {
            string text;
            try
            {
                text = await response.Content.ReadAsStringAsync(limit.Token);
            }
            catch (HttpRequestException error)
            {
                throw new ApiException(step, (int)response.StatusCode, error.Message);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ApiException(step, (int)response.StatusCode, $"no reply within {timeout.TotalSeconds:0} s");
            }

            if (!response.IsSuccessStatusCode)
                throw new ApiException(step, (int)response.StatusCode, Describe(text));

            if (string.IsNullOrWhiteSpace(text))
                return allowEmpty ? new JsonObject() : throw new ApiException(step, (int)response.StatusCode, "no body");

            try
            {
                return JsonNode.Parse(text) ?? new JsonObject();
            }
            catch (JsonException)
            {
                throw new ApiException(step, (int)response.StatusCode, "the reply is not JSON");
            }
        }
    }

    /// <summary>A failed reply's error code, detail or title; failed replies carry no credential.</summary>
    private static string Describe(string text)
    {
        try
        {
            var reply = JsonNode.Parse(text);
            return reply?["error"]?.ToString() ?? reply?["detail"]?.ToString() ?? reply?["title"]?.ToString() ?? "no detail";
        }
        catch (JsonException)
        {
            return "no detail";
        }
    }

    private static bool IsAuthorized(JsonNode reply) =>
        string.Equals(reply["state"]?.ToString(), "authorized", StringComparison.OrdinalIgnoreCase);

    /// <summary>A reply whose <c>state</c> is not <c>authorized</c> is an <see cref="ApiException"/> carrying that state.</summary>
    private static void RequireAuthorized(JsonNode reply, string step)
    {
        if (IsAuthorized(reply)) return;

        string state = reply["state"]?.ToString() ?? "no state";
        throw new ApiException(step, 200, reply["error"] is { } error ? $"{state}: {error}" : state) { State = state };
    }

    private static string Required(JsonNode reply, string property, string step) =>
        Text(reply, property) ?? throw new ApiException(step, 200, $"the reply has no {property}{WithError(reply)}");

    /// <summary>A non-empty string property of a reply, or null.</summary>
    private static string? Text(JsonNode reply, string property) =>
        reply[property] is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0 ? text : null;

    /// <summary>A time property of a reply, or null when it is missing or not a time.</summary>
    private static DateTimeOffset? Time(JsonNode reply, string property) =>
        Text(reply, property) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset time)
            ? time
            : null;

    /// <summary>A 2xx reply's own <c>error</c>, for the message of a reply missing what was expected.</summary>
    private static string WithError(JsonNode reply) => reply["error"] is { } error ? $", error {error}" : "";

    private static DateTimeOffset RequiredTime(JsonNode reply, string property, string step) =>
        DateTimeOffset.TryParse(Required(reply, property, step), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset time)
            ? time
            : throw new ApiException(step, 200, $"the reply's {property} is not a time");

    private static string[] Strings(JsonNode reply, string property, string step)
    {
        if (reply[property] is not JsonArray array)
            throw new ApiException(step, 200, $"the reply has no {property}");

        string[] strings = new string[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            strings[i] = array[i] is JsonValue value && value.TryGetValue(out string? text)
                ? text
                : throw new ApiException(step, 200, $"the reply's {property} holds a non-string");
        }

        return strings;
    }
}

/// <summary>A REST call that failed: its step, the HTTP status (0 when no reply came) and the reply's error or detail.</summary>
public sealed class ApiException(string step, int status, string detail)
    : Exception($"{step} failed{(status == 0 ? "" : $" ({status})")}: {detail}")
{
    /// <summary>The call that failed: <c>authenticate</c>, <c>code</c>, <c>token</c>, <c>game-ticket</c>,
    /// <c>provider-attempt</c>, <c>redeem</c>, <c>join</c>, <c>refresh</c>, <c>logout</c>, <c>create-run</c> or
    /// <c>delete-run</c>.</summary>
    public string Step { get; } = step;

    /// <summary>The HTTP status; 0 when no reply came (a transport failure or the timeout).</summary>
    public int Status { get; } = status;

    /// <summary>The reply's error code (<c>ACTIVE_GAME_SESSION</c>, ...), problem detail or title.</summary>
    public string Detail { get; } = detail;

    /// <summary>
    /// For a reply that answered but did not authorize (<c>state</c> other than <c>authorized</c>), that state:
    /// <c>revoked</c>, <c>expired</c>, ... (<c>no state</c> when it had none); null for any other failure.
    /// </summary>
    public string? State { get; init; }
}
