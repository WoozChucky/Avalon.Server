using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Avalon.Crashes.Worker.Storage;

public sealed class S3CrashObjectStore : ICrashObjectStore, IDisposable
{
    public const int PageSize = 100;
    private static readonly TimeSpan s_operationTimeout = TimeSpan.FromSeconds(30);
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly bool _ownsClient;
    private readonly TimeProvider _timeProvider;

    public S3CrashObjectStore(IAmazonS3 client, CrashStorageOptions options, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Bucket);
        _client = client;
        _bucket = options.Bucket;
    }

    public S3CrashObjectStore(CrashStorageOptions options, TimeProvider? timeProvider = null) : this(CreateClient(options), options, timeProvider) => _ownsClient = true;

    private static AmazonS3Client CreateClient(CrashStorageOptions options)
    {
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(options.Region) || string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            throw new CrashStorageException(CrashFailureCode.ConfigurationInvalid);
        }
        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = endpoint.AbsoluteUri,
            AuthenticationRegion = options.Region,
            ForcePathStyle = true,
            Timeout = TimeSpan.FromSeconds(30),
            MaxErrorRetry = 2
        });
    }

    public async Task<CrashObjectPage> ListAsync(string prefix, string? delimiter, string? continuationToken, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(s_operationTimeout, _timeProvider);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            ListObjectsV2Response response = await _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                Delimiter = delimiter,
                ContinuationToken = continuationToken,
                MaxKeys = PageSize
            }, operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            if ((response.S3Objects?.Count ?? 0) + (response.CommonPrefixes?.Count ?? 0) > PageSize
                || response.NextContinuationToken?.Length > 4096
                || response.IsTruncated == true && string.IsNullOrEmpty(response.NextContinuationToken))
            {
                throw new CrashStorageException(CrashFailureCode.StorageUnavailable);
            }
            StoredCrashObject[] objects = (response.S3Objects ?? []).Select(x => Fingerprint(x.Key, x.ETag, x.Size, x.LastModified)).ToArray();
            return new CrashObjectPage(objects, response.CommonPrefixes ?? [], response.IsTruncated == true ? response.NextContinuationToken : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new CrashStorageException(CrashFailureCode.StorageUnavailable);
        }
        catch (AmazonS3Exception e) when (continuationToken is not null && e.StatusCode == HttpStatusCode.BadRequest
            && e.ErrorCode is "InvalidArgument" or "InvalidToken" or "InvalidContinuationToken")
        {
            throw new CrashContinuationTokenException();
        }
        catch (Exception e) when (IsStorageError(e))
        {
            throw Failure(e);
        }
    }

    public async Task<StoredCrashObject?> HeadAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(s_operationTimeout, _timeProvider);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            GetObjectMetadataResponse response = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = key }, operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            return Fingerprint(key, response.ETag, response.ContentLength, response.LastModified);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new CrashStorageException(CrashFailureCode.StorageUnavailable);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception e) when (IsStorageError(e))
        {
            throw Failure(e);
        }
    }

    public async Task DownloadAsync(StoredCrashObject expected, string destination, long maxBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        long cap = Math.Min(maxBytes, CrashLimits.ArchiveBytes);
        if (expected.Size > cap)
            throw new CrashStorageException(CrashFailureCode.ArchiveTooLarge);
        if (expected.Size < 0)
            throw new CrashStorageException(CrashFailureCode.ObjectChanged);

        using var deadline = new CancellationTokenSource(s_operationTimeout, _timeProvider);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        bool created = false;
        bool completed = false;
        try
        {
            using GetObjectResponse response = await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucket,
                Key = expected.Key,
                EtagToMatch = expected.ETag
            }, operation.Token).ConfigureAwait(false);
            operation.Token.ThrowIfCancellationRequested();
            if (Fingerprint(expected.Key, response.ETag, response.ContentLength, response.LastModified) != expected)
                throw new CrashStorageException(CrashFailureCode.ObjectChanged);
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                created = true;
                byte[] buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    int read;
                    try
                    {
                        read = await response.ResponseStream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, cap - total + 1)), operation.Token).ConfigureAwait(false);
                    }
                    catch (IOException)
                    {
                        // Only response reads are transport I/O. Local file failures retain their type.
                        operation.Token.ThrowIfCancellationRequested();
                        throw new CrashStorageException(CrashFailureCode.StorageUnavailable);
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    if (read == 0)
                        break;
                    total += read;
                    if (total > cap)
                        throw new CrashStorageException(CrashFailureCode.ArchiveTooLarge);
                    await output.WriteAsync(buffer.AsMemory(0, read), operation.Token).ConfigureAwait(false);
                }
                if (total != expected.Size)
                    throw new CrashStorageException(CrashFailureCode.ObjectChanged);
            }
            completed = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new CrashStorageException(CrashFailureCode.StorageUnavailable);
        }
        catch (Exception e) when (IsStorageError(e))
        {
            throw Failure(e);
        }
        finally
        {
            if (created && !completed)
                File.Delete(destination);
        }
    }

    private static StoredCrashObject Fingerprint(string key, string? etag, long? size, DateTime? modified)
    {
        if (string.IsNullOrEmpty(etag) || size is null or < 0 || modified is null)
            throw new CrashStorageException(CrashFailureCode.StorageUnavailable);
        // Garage LIST preserves fractions while HEAD/GET HTTP dates have whole-second precision.
        // Floor consistently so identity comparisons work and retention can only expire earlier.
        long ticks = modified.Value.Ticks - modified.Value.Ticks % TimeSpan.TicksPerSecond;
        return new StoredCrashObject(key, etag, size.Value, new DateTimeOffset(ticks, TimeSpan.Zero));
    }

    private static bool IsStorageError(Exception e) => e is AmazonServiceException or AmazonClientException or HttpRequestException;
    private static CrashStorageException Failure(Exception e) => new(e is AmazonS3Exception s3 ? s3.StatusCode switch
    {
        HttpStatusCode.NotFound => CrashFailureCode.ObjectMissing,
        HttpStatusCode.PreconditionFailed => CrashFailureCode.ObjectChanged,
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => CrashFailureCode.AccessDenied,
        _ => CrashFailureCode.StorageUnavailable
    } : CrashFailureCode.StorageUnavailable);

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
