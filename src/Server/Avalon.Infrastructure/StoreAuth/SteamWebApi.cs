using System.Globalization;
using System.Net;
using System.Text.Json;
using Avalon.Common.GameAuth;
using Avalon.Configuration;

namespace Avalon.Infrastructure.StoreAuth;

/// <summary>Fixed-host, bounded transport. Never log an HTTP exception, URL or provider body containing credentials.</summary>
internal static class SteamWebApi
{
    private const int MaxResponseBytes = GameAuthPolicy.MaximumBodyBytes;
    private const int MaximumAttempts = 2;
    private const int MaximumSteamIdCharacters = 20;
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_retryDelay = TimeSpan.FromMilliseconds(100);
    private const int JsonDepth = 8;
    private const string Origin = "https://partner.steam-api.com/";
    internal const string AuthenticateTicketPath = "ISteamUserAuth/AuthenticateUserTicket/v1/";
    internal const string CheckOwnershipPath = "ISteamUser/CheckAppOwnership/v4/";
    internal static Uri Request(string endpoint, StoreAuthenticationConfiguration config, uint appId, params (string Key, string Value)[] fields)
    {
        var parameters = new List<(string Key, string Value)>
        {
            ("key", config.SteamPublisherKey), ("appid", appId.ToString(CultureInfo.InvariantCulture)),
        };
        parameters.AddRange(fields);
        return new Uri(Origin + endpoint + "?" + string.Join("&", parameters.Select(
            x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))));
    }

    internal static bool IsSteamId(string? value) => value is { Length: > 0 and <= MaximumSteamIdCharacters } &&
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id != 0 &&
        string.Equals(id.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

    internal static async Task<(bool Available, JsonDocument? Document)> GetAsync(HttpClient client, Uri uri,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(s_requestTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                    return (true, await ReadDocumentAsync(response, bounded.Token));
                if ((response.StatusCode != HttpStatusCode.TooManyRequests && response.StatusCode != HttpStatusCode.RequestTimeout &&
                     (int)response.StatusCode < 500) || attempt == MaximumAttempts - 1)
                {
                    return (false, null);
                }
            }
            catch (OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt == MaximumAttempts - 1) return (false, null);
            }
            catch (HttpRequestException) { if (attempt == MaximumAttempts - 1) return (false, null); }
            catch (IOException) { if (attempt == MaximumAttempts - 1) return (false, null); }
            await Task.Delay(s_retryDelay, cancellationToken);
        }
        return (false, null);
    }

    private static async Task<JsonDocument?> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes) return null;
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        byte[] buffer = new byte[MaxResponseBytes + 1];
        int used = 0;
        while (used < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(used), cancellationToken);
            if (read == 0) break;
            used += read;
        }
        if (used == 0 || used > MaxResponseBytes) return null;
        try { return JsonDocument.Parse(buffer.AsMemory(0, used), new JsonDocumentOptions { MaxDepth = JsonDepth }); }
        catch (JsonException) { return null; }
    }

    internal static bool Object(JsonElement element, string name, out JsonElement result)
    {
        result = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out result) && result.ValueKind == JsonValueKind.Object;
    }

    internal static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static bool? Boolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
