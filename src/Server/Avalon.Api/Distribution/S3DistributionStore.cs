using System.Net;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Avalon.Api.Distribution;

/// <summary>
/// The distribution bucket over S3 (Garage). Two clients: one reads over the in-cluster endpoint;
/// the other only presigns, for the public host, because an S3 signature covers the host the
/// request carries. Presigning is local and makes no network call.
/// </summary>
public sealed class S3DistributionStore : IDistributionStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AmazonS3Client _reader;
    private readonly AmazonS3Client _presigner;
    private readonly string _bucket;

    public S3DistributionStore(DistributionConfiguration config)
    {
        var credentials = new BasicAWSCredentials(config.AccessKeyId, config.SecretAccessKey);
        _reader = new AmazonS3Client(credentials, Settings(config.Endpoint, config.Region));
        _presigner = new AmazonS3Client(credentials, Settings(config.PublicUrl, config.Region));
        _bucket = config.Bucket;
    }

    private static AmazonS3Config Settings(string serviceUrl, string region) => new()
    {
        ServiceURL = serviceUrl,
        ForcePathStyle = true,
        AuthenticationRegion = region,
    };

    public async Task<ChannelPointer?> GetPointerAsync(Channel channel, CancellationToken ct)
    {
        string? json = await ReadAsync($"channels/{channel.Wire()}.json", ct);
        if (json is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<ChannelPointer>(json, Json)
                ?? throw new DistributionUnavailableException($"The {channel.Wire()} channel pointer is empty.");
        }
        catch (JsonException)
        {
            throw new DistributionUnavailableException($"The {channel.Wire()} channel pointer is not valid.");
        }
    }

    public async Task<StoredManifest?> GetManifestAsync(string manifestKey, CancellationToken ct)
    {
        string? json = await ReadAsync(manifestKey, ct);
        if (json is null)
            return null;
        // Published order is manifest, .sig, pointer: a manifest without its signature is still
        // being published, and an unsigned one would only make every launcher refuse the update.
        string? signature = await ReadAsync(manifestKey + ".sig", ct);
        if (string.IsNullOrWhiteSpace(signature))
            return null;
        return new StoredManifest(ParseManifest(json), json, signature.Trim());
    }

    public async Task<IReadOnlyList<StoredObject>> ListManifestsAsync(Channel channel, CancellationToken ct)
    {
        var found = new List<StoredObject>();
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = $"manifests/{channel.Wire()}/" };
        ListObjectsV2Response response;
        do
        {
            response = await Guarded(() => _reader.ListObjectsV2Async(request, ct));
            foreach (S3Object obj in response.S3Objects ?? [])
            {
                if (obj.Key.EndsWith(".json", StringComparison.Ordinal))
                    found.Add(new StoredObject(obj.Key, new DateTimeOffset(obj.LastModified ?? DateTime.UnixEpoch, TimeSpan.Zero)));
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        return found;
    }

    public async Task<IReadOnlyList<StoredObject>> ListChangelogAsync(string prefix, CancellationToken ct)
    {
        var found = new List<StoredObject>();
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix };
        ListObjectsV2Response response;
        do
        {
            response = await Guarded(() => _reader.ListObjectsV2Async(request, ct));
            foreach (S3Object obj in response.S3Objects ?? [])
            {
                if (obj.Key.EndsWith(".json", StringComparison.Ordinal))
                    found.Add(new StoredObject(obj.Key, new DateTimeOffset(obj.LastModified ?? DateTime.UnixEpoch, TimeSpan.Zero)));
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);

        return found;
    }

    public async Task<ChangelogEntryDto?> GetChangelogEntryAsync(string key, CancellationToken ct)
    {
        string? json = await ReadAsync(key, ct);
        return json is null ? null : ParseChangelog(json);
    }

    public async Task<LauncherRelease?> GetLauncherAsync(CancellationToken ct)
    {
        string? json = await ReadAsync("launcher/latest.json", ct);
        return json is null ? null : ParseLauncher(json);
    }

    public Uri Presign(string objectKey, TimeSpan ttl) =>
        new(_presigner.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Protocol = Protocol.HTTPS,
            Expires = DateTime.UtcNow.Add(ttl),
        }));

    /// <exception cref="DistributionUnavailableException">The schema is not one this API understands.</exception>
    public static ManifestDocument ParseManifest(string json)
    {
        ManifestDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<ManifestDocument>(json, Json);
        }
        catch (JsonException)
        {
            throw new DistributionUnavailableException("A published manifest is not valid JSON.");
        }

        if (doc is null || doc.Schema != 1)
            throw new DistributionUnavailableException("A published manifest has a schema this server does not understand.");
        return doc;
    }

    /// <summary>
    /// A changelog entry, or null when it is not one this API understands (bad JSON, another schema, a
    /// required field missing): one unreadable entry is left out of the feed rather than failing it.
    /// </summary>
    public static ChangelogEntryDto? ParseChangelog(string json)
    {
        RawChangelogEntry? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawChangelogEntry>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (raw is not { Schema: 1, Product: { } product, Version: { } version, PublishedAt: { } publishedAt, Items: { } items })
            return null;
        // The website renders these: whole items only, and links only when they are absolute https.
        var kept = items
            .Where(i => i is { Kind: not null, Text: not null })
            .Select(i => i with { PrUrl = Https(i.PrUrl) })
            .ToList();
        return new ChangelogEntryDto(product, raw.Channel, version, raw.Build, publishedAt, Https(raw.ReleaseUrl), kept);
    }

    private static Uri? Https(Uri? url) => url is { IsAbsoluteUri: true, Scheme: "https" } ? url : null;

    // The stored shape (spec §4); its commit is only for the next release's range, not for the website.
    private sealed record RawChangelogEntry(int Schema, string? Product, string? Channel, string? Version, string? Build,
        DateTimeOffset? PublishedAt, Uri? ReleaseUrl, List<ChangelogItemDto>? Items);

    /// <summary><c>launcher/latest.json</c>: Tauri's static updater format plus an <c>installer</c> block.</summary>
    public static LauncherRelease ParseLauncher(string json)
    {
        try
        {
            return ReadLauncher(json);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new DistributionUnavailableException("The published launcher release is not valid.");
        }
    }

    private static LauncherRelease ReadLauncher(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        JsonElement windows = root.GetProperty("platforms").GetProperty("windows-x86_64");
        JsonElement installer = root.GetProperty("installer");
        return new LauncherRelease(
            root.GetProperty("version").GetString()!,
            root.TryGetProperty("notes", out JsonElement notes) ? notes.GetString() ?? "" : "",
            root.GetProperty("pub_date").GetDateTimeOffset(),
            windows.GetProperty("signature").GetString()!,
            windows.GetProperty("url").GetString()!,
            installer.GetProperty("key").GetString()!,
            installer.GetProperty("size").GetInt64(),
            installer.GetProperty("sha256").GetString()!);
    }

    private async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        return await Guarded(async () =>
        {
            try
            {
                using GetObjectResponse response = await _reader.GetObjectAsync(_bucket, key, ct);
                using var reader = new StreamReader(response.ResponseStream);
                return await reader.ReadToEndAsync(ct);
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                return (string?)null;
            }
        });
    }

    /// <summary>
    /// Storage down or refusing, for a read or a listing: an outage (503), as for the database and
    /// Redis, not a 500. The cause is kept as the inner exception.
    /// </summary>
    private static async Task<T> Guarded<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw new DistributionUnavailableException("Downloads are not available right now.", e);
        }
    }

    public void Dispose()
    {
        _reader.Dispose();
        _presigner.Dispose();
    }
}
