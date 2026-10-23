using System.Globalization;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Validation;
using Avalon.Crashes.Worker.Storage;

namespace Avalon.Crashes.Worker.Discovery;

public sealed class CrashDiscovery(ICrashObjectStore objects, ICrashDiscoveryStore store, ICrashQueue queue)
{
    public async Task RunCycleAsync(DateTimeOffset now, CancellationToken ct)
    {
        now = now.ToUniversalTime();
        DiscoveryCursor cursor = await store.LoadAsync(ct).ConfigureAwait(false);
        var versions = cursor.Versions.Take(DiscoveryCursor.VersionBatchSize).ToList();
        // Root pagination progresses independently of uploads under any existing version.
        try
        {
            CrashObjectPage root = await objects.ListAsync("", "/", cursor.ContinuationToken, ct).ConfigureAwait(false);
            await DiscoverPageAsync(root, now, ct).ConfigureAwait(false);
            string[] found = root.CommonPrefixes.Where(x => x.EndsWith('/') && CrashObjectKey.IsVersion(x[..^1]))
                .Select(x => x[..^1]).Distinct(StringComparer.Ordinal).Take(DiscoveryCursor.VersionBatchSize).ToArray();
            cursor.DiscoveredVersions = found;
            foreach (string version in found)
            {
                if (versions.Count < DiscoveryCursor.VersionBatchSize && !versions.Any(x => string.Equals(x.Version, version, StringComparison.Ordinal)))
                    versions.Add(new DiscoveryVersion { Version = version });
            }
            cursor.ContinuationToken = root.NextContinuationToken;
        }
        catch (CrashContinuationTokenException)
        {
            cursor.ContinuationToken = null;
        }
        cursor.Versions = versions;
        cursor.LastScanAt = now;
        await store.SaveAsync(cursor, ct).ConfigureAwait(false);
        cursor.DiscoveredVersions = [];

        int used = 1;
        bool inventoryComplete = false;
        // Root refresh plus 24 flat pages reserve a quarter of the 100 listing requests.
        // Flat inventory also sees nonconforming names and uploads under historical dates.
        while (used < CrashDiscoveryOptions.BackfillBudget && !inventoryComplete)
        {
            inventoryComplete = await InventoryPageAsync(cursor, now, ct).ConfigureAwait(false);
            used++;
        }
        int index = 0;
        while (used < CrashDiscoveryOptions.PageBudget)
        {
            if (versions.Count == 0)
            {
                if (inventoryComplete)
                    break;
                inventoryComplete = await InventoryPageAsync(cursor, now, ct).ConfigureAwait(false);
            }
            else
            {
                DiscoveryVersion version = versions[index++ % versions.Count];
                await RecentPageAsync(cursor, version, now, ct).ConfigureAwait(false);
            }
            used++;
        }
    }

    private async Task<bool> InventoryPageAsync(DiscoveryCursor cursor, DateTimeOffset now, CancellationToken ct)
    {
        bool complete = false;
        try
        {
            CrashObjectPage page = await objects.ListAsync("", null, cursor.BackfillContinuationToken, ct).ConfigureAwait(false);
            await DiscoverPageAsync(page, now, ct).ConfigureAwait(false);
            cursor.BackfillContinuationToken = page.NextContinuationToken;
            complete = page.NextContinuationToken is null;
            if (complete)
                cursor.LastBackfillAt = now;
        }
        catch (CrashContinuationTokenException)
        {
            cursor.BackfillContinuationToken = null;
        }
        // A partial page failure never persists its next token. Restart replays durable inserts.
        await store.SaveAsync(cursor, ct).ConfigureAwait(false);
        return complete;
    }

    private async Task RecentPageAsync(DiscoveryCursor cursor, DiscoveryVersion version, DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (version.RecentDate != today)
        {
            version.YesterdayToken = version.RecentDate == today.AddDays(-1) ? version.TodayToken : null;
            version.TodayToken = null;
            version.RecentDate = today;
            version.Phase = 0;
        }
        version.Dates = version.Dates.Where(x => x.Date >= today.AddDays(-91) && x.Date <= today.AddDays(-2)).ToArray();
        if (version.HistoricalDate is { } active && !version.Dates.Any(x => x.Date == active))
        {
            version.HistoricalDate = null;
            version.HistoricalToken = null;
        }
        if (version.Phase == 3)
        {
            // Inventory real date prefixes first; never probe missing days in a sparse version.
            if (!version.DateInventoryReady || version.Dates.Count == 0)
            {
                version.Phase = 0;
            }
            else
            {
                version.HistoricalDate ??= version.Dates.OrderBy(x => x.LastScannedAt != null).ThenBy(x => x.LastScannedAt)
                    .ThenByDescending(x => x.Date).Select(x => (DateOnly?)x.Date).First();
            }
        }
        string versionPrefix = version.Version + "/";
        string prefix = versionPrefix;
        string? token = version.Phase switch
        {
            0 => version.DatePrefixToken,
            1 => version.TodayToken,
            2 => version.YesterdayToken,
            _ => version.HistoricalToken
        };
        if (version.Phase != 0)
        {
            DateOnly date = version.Phase switch { 1 => today, 2 => today.AddDays(-1), _ => version.HistoricalDate!.Value };
            prefix += date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/";
        }
        string? next;
        CrashObjectPage? page = null;
        try
        {
            page = await objects.ListAsync(prefix, version.Phase == 0 ? "/" : null, token, ct).ConfigureAwait(false);
            await DiscoverPageAsync(page, now, ct).ConfigureAwait(false);
            next = page.NextContinuationToken;
        }
        catch (CrashContinuationTokenException)
        {
            next = null;
        }
        switch (version.Phase)
        {
            case 0:
                version.DatePrefixToken = next;
                if (page is not null)
                {
                    version.DateInventoryReady |= next is null;
                    DiscoveryDatePrefix[] found = page.CommonPrefixes
                        .Where(x => x.Length > versionPrefix.Length && x.StartsWith(versionPrefix, StringComparison.Ordinal) && x.EndsWith('/'))
                        .Select(x => DateOnly.TryParseExact(x.AsSpan(versionPrefix.Length, x.Length - versionPrefix.Length - 1), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ? (DateOnly?)date : null)
                        .Where(x => x >= today.AddDays(-91) && x <= today.AddDays(-2)).Select(x => new DiscoveryDatePrefix(version.Version, x!.Value)).Distinct().ToArray();
                    cursor.DiscoveredDates = found;
                    version.Dates = version.Dates.Concat(found.Where(x => !version.Dates.Any(d => d.Date == x.Date)).Select(x => new DiscoveryDate(x.Date, null))).ToArray();
                }
                break;
            case 1: version.TodayToken = next; break;
            case 2: version.YesterdayToken = next; break;
            default:
                version.HistoricalToken = next;
                if (page is not null && next is null)
                {
                    DateOnly completed = version.HistoricalDate!.Value;
                    cursor.CompletedDates = [new DiscoveryDatePrefix(version.Version, completed)];
                    version.Dates = version.Dates.Select(x => x.Date == completed ? x with { LastScannedAt = now } : x).ToArray();
                    version.HistoricalDate = null;
                }
                break;
        }
        // One page per version turn, including continued historical object pages.
        version.Phase = (version.Phase + 1) % 4;
        version.LastVisitedAt = now;
        cursor.UpdatedVersions = [version];
        await store.SaveAsync(cursor, ct).ConfigureAwait(false);
        cursor.UpdatedVersions = [];
        cursor.DiscoveredDates = [];
        cursor.CompletedDates = [];
    }

    private async Task DiscoverPageAsync(CrashObjectPage page, DateTimeOffset now, CancellationToken ct)
    {
        foreach (StoredCrashObject source in page.Objects)
        {
            ct.ThrowIfCancellationRequested();
            if (source.LastModified.ToUniversalTime() > now.AddDays(-90))
                await queue.DiscoverAsync(source, now, ct).ConfigureAwait(false);
        }
    }
}
