using System.Globalization;

namespace Avalon.Api.Distribution;

public sealed record CrashNoticeRequest(string? Key);

/// <summary>
/// The anonymous upload-complete notice (#1252, Client#459): a client that finished its PUT names the slot's key, and
/// the key goes on to the crash worker, which HEADs the object before it queues anything. This API cannot read the
/// crash bucket, so it only checks the key could be one of its own slots from today or yesterday, and answers alike
/// whether or not the worker was reached: the answer says nothing about what exists.
/// </summary>
public sealed class CrashNoticeService(ICrashNoticeForwarder forwarder, TimeProvider time)
{
    /// <summary>False for a key no slot of this API could have (a 400); true for one passed on, or not.</summary>
    public async Task<bool> NoticeAsync(string? key, CancellationToken ct)
    {
        if (key is null || !IsRecentSlotKey(key, time.GetUtcNow()))
            return false;
        try
        {
            await forwarder.ForwardAsync(key, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // The worker's poll finds the report anyway; a lost notice only delays it.
        }
        return true;
    }

    /// <summary>
    /// The slot key <see cref="CrashReportService" /> issues, <c>&lt;version&gt;/&lt;yyyy-MM-dd&gt;/&lt;32 hex&gt;.zip</c>
    /// (the worker's <c>CrashObjectKey</c> reads the same shape), dated today or yesterday in UTC.
    /// </summary>
    internal static bool IsRecentSlotKey(string key, DateTimeOffset now)
    {
        string[] parts = key.Split('/');
        if (parts.Length != 3 || parts[0] is not { Length: > 0 and <= 64 } version || version is "." or ".."
            || !version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+')
            || !DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            || parts[2].Length != 36 || !parts[2].EndsWith(".zip", StringComparison.Ordinal)
            || !parts[2].AsSpan(0, 32).ContainsOnlyHexLower())
        {
            return false;
        }
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return date == today || date == today.AddDays(-1);
    }
}

file static class HexSpan
{
    public static bool ContainsOnlyHexLower(this ReadOnlySpan<char> text)
    {
        foreach (char c in text)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                return false;
        }
        return true;
    }
}

/// <summary>Passes a notice's key on to the crash worker.</summary>
public interface ICrashNoticeForwarder
{
    Task ForwardAsync(string key, CancellationToken ct);
}

/// <summary>No worker URL configured: notices are accepted and go nowhere; the worker's poll finds every report.</summary>
public sealed class UnconfiguredCrashNoticeForwarder : ICrashNoticeForwarder
{
    public Task ForwardAsync(string key, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>POSTs the key to the worker's internal notice endpoint, bounded by a short timeout.</summary>
public sealed class HttpCrashNoticeForwarder(HttpClient http, DistributionConfiguration configuration) : ICrashNoticeForwarder
{
    public async Task ForwardAsync(string key, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(TimeSpan.FromMilliseconds(configuration.CrashNoticeTimeoutMs));
        using HttpResponseMessage response = await http.PostAsJsonAsync(new Uri(configuration.CrashNoticeUrl), new { key }, bounded.Token);
    }
}
