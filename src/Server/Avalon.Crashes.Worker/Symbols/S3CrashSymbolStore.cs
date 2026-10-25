using System.Net;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Avalon.Crashes.Worker.Storage;

namespace Avalon.Crashes.Worker.Symbols;

public sealed class S3CrashSymbolStore : ICrashSymbolStore, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly CrashSymbolOptions _options;
    private readonly TimeProvider _time;
    private readonly bool _ownsClient;

    public S3CrashSymbolStore(IAmazonS3 client, CrashSymbolOptions options, TimeProvider? timeProvider = null)
    {
        options.Validate();
        _client = client;
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
    }

    public S3CrashSymbolStore(CrashStorageOptions storage, CrashSymbolOptions options, TimeProvider? timeProvider = null)
        : this(CreateClient(storage), options, timeProvider) => _ownsClient = true;

    private static AmazonS3Client CreateClient(CrashStorageOptions storage)
    {
        if (!Uri.TryCreate(storage.Endpoint, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != "https"
            || endpoint.UserInfo.Length > 0 || string.IsNullOrWhiteSpace(storage.Region)
            || string.IsNullOrWhiteSpace(storage.AccessKeyId) || string.IsNullOrWhiteSpace(storage.SecretAccessKey))
        {
            throw new CrashSymbolException(CrashFailureCode.ConfigurationInvalid);
        }
        return new AmazonS3Client(new BasicAWSCredentials(storage.AccessKeyId, storage.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = endpoint.AbsoluteUri,
            AuthenticationRegion = storage.Region,
            ForcePathStyle = true,
            // Garage rejects the SDK's optional streaming checksum trailer. SigV4 payload
            // signing and the cache's full SHA-256/length/identity checks remain enabled.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2
        });
    }

    public async Task DownloadOriginalAsync(ModuleIdentity identity, bool pdb, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct)
    {
        SymbolFiles.Require(identity);
        string name = pdb ? identity.PdbName! : identity.Name;
        string id = pdb ? identity.PdbKey! : identity.ImageKey;
        await DownloadAsync(_options.OriginalBucket, $"{name}/{id}/{name}", destination, maxBytes, budget, false, ct);
    }

    public Task<SymbolCacheMetadata?> DownloadConvertedAsync(ModuleIdentity identity, string cacheVersion, string destination, long maxBytes, SymbolDownloadBudget budget, CancellationToken ct)
    {
        SymbolFiles.Require(identity);
        if (!SymbolFiles.Segment(cacheVersion))
            throw new CrashSymbolException(CrashFailureCode.ConfigurationInvalid);
        return DownloadAsync(_options.ConvertedBucket, $"{cacheVersion}/{SymbolFiles.Relative(identity)}", destination, maxBytes, budget, true, ct);
    }

    private async Task<SymbolCacheMetadata?> DownloadAsync(string bucket, string key, string destination, long maxBytes, SymbolDownloadBudget budget, bool converted, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        budget.RestrictTo(_options.DownloadBytes);
        long cap = Math.Min(Math.Min(maxBytes, _options.ArtifactBytes), budget.RemainingBytes);
        if (cap <= 0)
            throw new CrashSymbolException(CrashFailureCode.ResourceLimit);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _time);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        bool completed = false, created = false;
        try
        {
            using GetObjectResponse response = await _client.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (response.ContentLength is < 0 || response.ContentLength > cap)
                throw new CrashSymbolException(CrashFailureCode.ResourceLimit);
            SymbolCacheMetadata? metadata = null;
            if (converted)
            {
                string? json = response.Metadata["x-amz-meta-symbol"];
                if (json is { Length: <= 4096 })
                {
                    try { metadata = JsonSerializer.Deserialize<SymbolCacheMetadata>(json); }
                    catch (JsonException) { }
                }
            }
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                created = true;
                await SymbolFiles.CopyAsync(response.ResponseStream, output, cap, budget, operation.Token);
            }
            operation.Token.ThrowIfCancellationRequested();
            if (new FileInfo(destination).Length != response.ContentLength)
                throw new CrashSymbolException(CrashFailureCode.StorageUnavailable);
            completed = true;
            return metadata;
        }
        catch (AmazonS3Exception e) when (converted && e.StatusCode == HttpStatusCode.NotFound)
        {
            // An empty miss placeholder satisfies the quota adapter's completed-file contract; the resolver deletes it.
            await using var empty = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            completed = true;
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new CrashSymbolException(CrashFailureCode.StorageUnavailable);
        }
        catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            ct.ThrowIfCancellationRequested();
            throw Failure(e);
        }
        finally
        {
            if (created && !completed)
                File.Delete(destination);
        }
    }

    public async Task UploadConvertedAsync(ModuleIdentity identity, SymbolCacheMetadata metadata, string source, CancellationToken ct)
    {
        SymbolFiles.Require(identity);
        if (metadata.CacheVersion != _options.CacheVersion || metadata.ImageName != identity.Name || metadata.ImageKey != identity.ImageKey
            || metadata.DebugFile != identity.PdbName || metadata.DebugId != identity.PdbKey || metadata.ConverterVersion != CrashSymbolOptions.ConverterVersion
            || metadata.Bytes is <= 0 or > 256L * 1024 * 1024 || new FileInfo(source).Length != metadata.Bytes
            || await SymbolFiles.DigestAsync(source, ct) != metadata.Sha256)
        {
            throw new CrashSymbolException(CrashFailureCode.MissingRuntimeSymbols);
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), _time);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            await using FileStream input = File.OpenRead(source);
            var request = new PutObjectRequest
            {
                BucketName = _options.ConvertedBucket,
                Key = $"{metadata.CacheVersion}/{SymbolFiles.Relative(identity)}",
                InputStream = input,
                AutoCloseStream = false,
                ContentType = "text/plain"
            };
            request.Metadata["symbol"] = JsonSerializer.Serialize(metadata);
            await _client.PutObjectAsync(request, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new CrashSymbolException(CrashFailureCode.StorageUnavailable);
        }
        catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw Failure(e);
        }
    }

    private static CrashSymbolException Failure(Exception error) => new(error is AmazonS3Exception s3 ? s3.StatusCode switch
    {
        HttpStatusCode.NotFound => CrashFailureCode.MissingRuntimeSymbols,
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => CrashFailureCode.AccessDenied,
        _ => CrashFailureCode.StorageUnavailable
    } : CrashFailureCode.StorageUnavailable);

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
