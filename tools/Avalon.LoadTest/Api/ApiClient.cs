using System.Buffers.Text;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Avalon.LoadTest.Api;

/// <summary>
/// The REST calls a bot and the run commands make: the launcher's sign-in chain to a game context (as
/// <c>tools/Avalon.LocalDev</c> walks it), join tickets, context refresh and logout, and the admin load-test account
/// endpoints. Writes nothing to the console and never logs a context out on its own: a bot's context must outlive
/// each call. Every failure is an <see cref="ApiException"/> naming its step; a cancellation stays an
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
    /// password checks (BCrypt) on the API per call: sign a bot in once and refresh its context after.
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
        JsonNode redeemed = await SendAsync("redeem", HttpMethod.Post, "client/auth/handoffs/redeem", new
        {
            attemptCredential = Required(attempt, "attemptCredential", "provider-attempt"),
            handoffTicket = Required(ticket, "ticket", "game-ticket"),
        }, null, idempotencyKey: Guid.NewGuid(), ct);

        RequireAuthorized(redeemed, "redeem");
        return new GameContext
        {
            Credential = Required(redeemed, "gameContextCredential", "redeem"),
            RefreshToken = Required(redeemed, "gameContextRefreshToken", "redeem"),
            ContextExpiresAt = RequiredTime(redeemed, "contextExpiresAt", "redeem"),
            AuthorizationValidUntil = RequiredTime(redeemed, "authorizationValidUntil", "redeem"),
        };
    }

    /// <summary>
    /// A join ticket for world <paramref name="worldId"/>. <paramref name="confirmTakeover"/> replaces the account's
    /// live world session, which is otherwise refused (409, <c>ActiveGameSession</c>). A reply naming another world is
    /// refused here: the tool enters only the world it was told to. A timeout, transport failure or 5xx is retried twice
    /// under the same <c>Idempotency-Key</c>.
    /// </summary>
    public async Task<JoinTicket> JoinTicketAsync(GameContext context, ushort worldId, bool confirmTakeover,
        CancellationToken ct)
    {
        const string Step = "join";
        JsonNode reply = await SendWithRetriesAsync(Step, "game/join-tickets",
            new { gameContextCredential = context.Credential, worldId, confirmTakeover }, ct);
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
    /// Refreshes the context: both the credential and the refresh token rotate, and the expiries move on. A timeout,
    /// transport failure or 5xx is retried twice under the same <c>Idempotency-Key</c>.
    /// </summary>
    public async Task RefreshAsync(GameContext context, CancellationToken ct)
    {
        const string Step = "refresh";
        JsonNode reply = await SendWithRetriesAsync(Step, "client/auth/game-context/refresh",
            new { gameContextRefreshToken = context.RefreshToken }, ct);
        RequireAuthorized(reply, Step);
        string credential = Required(reply, "gameContextCredential", Step);
        string refreshToken = Required(reply, "gameContextRefreshToken", Step);
        DateTimeOffset contextExpiresAt = RequiredTime(reply, "contextExpiresAt", Step);
        DateTimeOffset authorizationValidUntil = RequiredTime(reply, "authorizationValidUntil", Step);

        context.Credential = credential;
        context.RefreshToken = refreshToken;
        context.ContextExpiresAt = contextExpiresAt;
        context.AuthorizationValidUntil = authorizationValidUntil;
    }

    /// <summary>Signs the game context out; a context already gone (401, 404) counts as signed out.</summary>
    public async Task LogoutAsync(GameContext context, CancellationToken ct)
    {
        try
        {
            await SendAsync("logout", HttpMethod.Post, "client/auth/game-context/logout",
                new { gameContextCredential = context.Credential }, null, idempotencyKey: null, ct, allowEmpty: true);
        }
        catch (ApiException error) when (error.Status is 401 or 404)
        {
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
    /// matters for a refresh: its refresh token is one-use, and a second key would find it already rotated.
    /// </summary>
    private async Task<JsonNode> SendWithRetriesAsync(string step, string path, object body, CancellationToken ct)
    {
        var key = Guid.NewGuid();
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await SendAsync(step, HttpMethod.Post, path, body, null, key, ct);
            }
            catch (ApiException error) when (attempt < s_retryDelays.Length && error.Status is 0 or >= 500)
            {
                await Task.Delay(s_retryDelays[attempt], ct);
            }
        }
    }

    private async Task<JsonNode> SendAsync(string step, HttpMethod method, string path, object body, string? bearer,
        Guid? idempotencyKey, CancellationToken ct, bool allowEmpty = false)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (idempotencyKey is { } key) request.Headers.Add("Idempotency-Key", key.ToString("D"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException error)
        {
            throw new ApiException(step, 0, error.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(step, 0, $"no reply within {_http.Timeout.TotalSeconds:0} s");
        }

        using (response)
        {
            string text;
            try
            {
                text = await response.Content.ReadAsStringAsync(ct);
            }
            catch (HttpRequestException error)
            {
                throw new ApiException(step, (int)response.StatusCode, error.Message);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ApiException(step, (int)response.StatusCode, $"no reply within {_http.Timeout.TotalSeconds:0} s");
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

    private static void RequireAuthorized(JsonNode reply, string step)
    {
        string state = reply["state"]?.ToString() ?? "no state";
        if (!state.Equals("authorized", StringComparison.OrdinalIgnoreCase))
            throw new ApiException(step, 200, reply["error"] is { } error ? $"{state}: {error}" : state);
    }

    private static string Required(JsonNode reply, string property, string step) =>
        reply[property] is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0
            ? text
            : throw new ApiException(step, 200, $"the reply has no {property}{WithError(reply)}");

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

    /// <summary>The reply's error code (<c>ActiveGameSession</c>, ...), problem detail or title.</summary>
    public string Detail { get; } = detail;
}
