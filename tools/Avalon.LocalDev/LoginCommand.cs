using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Avalon.LocalDev;

/// <summary>
/// <c>login</c> and <c>check</c>: the launcher's REST chain for a username and password (docs/development-setup.md).
/// <c>/account/authenticate</c> signs in; <c>/client/auth/code</c>, with a PKCE challenge and the password again, and
/// <c>/client/auth/token</c> open a launcher session; <c>/client/auth/game-ticket</c> issues the one-use game ticket the
/// game client redeems. <c>login</c> writes that ticket, one line, to standard output, or starts the client with it on
/// standard input (<c>--launch</c>). <c>check</c> goes on as the client would: a provider attempt, the handoff
/// redemption, the world list and a join ticket, then signs the game context out, and prints what it found but no
/// credential. Each run opens a new launcher session for the account.
/// </summary>
public static class LoginCommand
{
    private const string ProtocolVersion = "0.2.0";
    private const string ApplicationKey = "avalon.base";
    private const string TicketVariable = "AVALON_GAME_TICKET_STDIN";

    public static async Task<int> RunAsync(CommandLine options, bool check, CancellationToken cancellationToken)
    {
        string password = options.Password ?? Environment.GetEnvironmentVariable(CommandLine.PasswordVariable)
            ?? ReadPassword(options.User);
        using var http = new HttpClient { BaseAddress = options.Api, Timeout = TimeSpan.FromSeconds(30) };
        string ticket = await GameTicketAsync(http, options.User, password, cancellationToken);

        if (check)
            return await CheckAsync(http, ticket, options.World, cancellationToken);

        if (options.Launch is { } runtime)
            return Launch(runtime, ticket);

        await Console.Out.WriteLineAsync(ticket.AsMemory(), cancellationToken);
        return 0;
    }

    private static async Task<string> GameTicketAsync(HttpClient http, string user, string password,
        CancellationToken cancellationToken)
    {
        JsonNode signedIn = await PostAsync(http, "account/authenticate", new { username = user, password }, null, null,
            cancellationToken);
        string accessToken = signedIn["token"]?.GetValue<string>()
            ?? throw new LocalDevException(signedIn["mfaHash"] is not null
                ? $"{user} has MFA on, which this tool does not answer: use an account without it."
                : $"Signing in as {user} failed (status {signedIn["status"]}).");

        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        // The website would redirect to the launcher's loopback port; nothing listens here, the code comes back in the body.
        const int RedirectPort = 49152;
        JsonNode code = await PostAsync(http, "client/auth/code",
            new { challenge, redirectPort = RedirectPort, currentPassword = password }, accessToken, null, cancellationToken);
        JsonNode launcher = await PostAsync(http, "client/auth/token",
            new { code = Required(code, "code"), verifier, redirectPort = RedirectPort, deviceName = "Avalon.LocalDev" },
            null, null, cancellationToken);
        JsonNode ticket = await PostAsync(http, "client/auth/game-ticket", new { }, Required(launcher, "accessToken"), null,
            cancellationToken);
        Console.Error.WriteLine($"Signed in as {user}; game ticket issued.");
        return Required(ticket, "ticket");
    }

    private static async Task<int> CheckAsync(HttpClient http, string ticket, ushort? world,
        CancellationToken cancellationToken)
    {
        JsonNode attempt = await PostAsync(http, "client/auth/provider-attempts", new
        {
            applicationKey = ApplicationKey,
            protocolVersion = ProtocolVersion,
            clientRunId = Guid.NewGuid(),
            linkChallenge = Base64Url(RandomNumberGenerator.GetBytes(32)),
        }, null, null, cancellationToken);
        JsonNode context = await PostAsync(http, "client/auth/handoffs/redeem",
            new { attemptCredential = Required(attempt, "attemptCredential"), handoffTicket = ticket }, null, Guid.NewGuid(),
            cancellationToken);
        string credential = Required(context, "gameContextCredential");
        Console.Error.WriteLine($"Game context: {context["state"]}, license source {context["licenseSource"]}.");

        try
        {
            JsonArray worlds = (await PostAsync(http, "game/worlds", new { gameContextCredential = credential }, null, null,
                cancellationToken)).AsArray();
            if (worlds.Count == 0)
            {
                throw new LocalDevException("No world is listed. Is the world server running and ready, and does the " +
                                            "account hold a license (state Authorized above)?");
            }

            foreach (JsonNode? listed in worlds)
            {
                Console.Error.WriteLine($"World {listed?["worldId"]} {listed?["name"]}: {listed?["host"]}:{listed?["port"]}, " +
                                        $"TLS name {listed?["tlsServerName"]}, version {listed?["version"]}");
            }

            ushort chosen = world ?? worlds[0]!["worldId"]!.GetValue<ushort>();
            JsonNode join = await PostAsync(http, "game/join-tickets", new { gameContextCredential = credential, worldId = chosen },
                null, Guid.NewGuid(), cancellationToken);
            Required(join, "joinTicket");
            Console.Error.WriteLine($"Join ticket issued for world {chosen}, until {join["expiresAt"]}.");
            return 0;
        }
        finally
        {
            await PostAsync(http, "client/auth/game-context/logout", new { gameContextCredential = credential }, null, null,
                CancellationToken.None, allowEmpty: true);
        }
    }

    private static int Launch(string runtime, string ticket)
    {
        string path = Path.GetFullPath(runtime);
        var start = new ProcessStartInfo(path)
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        start.ArgumentList.Add("--channel");
        start.ArgumentList.Add("avalon");
        start.Environment[TicketVariable] = "1";
        using Process process = Process.Start(start) ?? throw new LocalDevException($"Could not start {path}.");
        process.StandardInput.Write(ticket + "\n");
        process.StandardInput.Close();
        Console.Error.WriteLine($"Started {path} (process {process.Id.ToString(CultureInfo.InvariantCulture)}).");
        return 0;
    }

    private static async Task<JsonNode> PostAsync(HttpClient http, string path, object body, string? bearer,
        Guid? idempotencyKey, CancellationToken cancellationToken, bool allowEmpty = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (idempotencyKey is { } key) request.Headers.Add("Idempotency-Key", key.ToString("D"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new LocalDevException($"POST {http.BaseAddress}{path} failed: {error.Message} Is the API running on " +
                                        $"{http.BaseAddress}, and is its development certificate trusted (dotnet dev-certs https --trust)?");
        }

        using (response)
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new LocalDevException(
                    $"POST /{path} answered {(int)response.StatusCode} {response.ReasonPhrase}: {Describe(text)}");
            }

            if (string.IsNullOrWhiteSpace(text))
                return allowEmpty ? new JsonObject() : throw new LocalDevException($"POST /{path} answered with no body.");

            return JsonNode.Parse(text) ?? new JsonObject();
        }
    }

    /// <summary>A failed reply's error code or title, never a credential: replies carry none on failure.</summary>
    private static string Describe(string text)
    {
        try
        {
            var reply = JsonNode.Parse(text);
            return reply?["error"]?.ToString() ?? reply?["title"]?.ToString() ?? reply?["detail"]?.ToString() ?? "no detail";
        }
        catch (System.Text.Json.JsonException)
        {
            return "no detail";
        }
    }

    private static string Required(JsonNode reply, string property) =>
        reply[property]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new LocalDevException($"The reply has no {property}: {Describe(reply.ToJsonString())}");

    private static string ReadPassword(string user)
    {
        if (Console.IsInputRedirected)
            throw new LocalDevException($"No password: pass --password or set {CommandLine.PasswordVariable}.");

        Console.Error.Write($"Password for {user}: ");
        var password = new StringBuilder();
        for (ConsoleKeyInfo key = Console.ReadKey(intercept: true); key.Key != ConsoleKey.Enter;
             key = Console.ReadKey(intercept: true))
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0) password.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return password.ToString();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
