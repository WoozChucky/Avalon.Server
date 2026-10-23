using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Avalon.Crashes.Worker.Storage;

public sealed class S3CrashObjectStore : ICrashObjectStore, IDisposable
{
    public const int PageSize = 100;
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly bool _ownsClient;

    public S3CrashObjectStore(IAmazonS3 client, CrashStorageOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Bucket);
        _client = client;
        _bucket = options.Bucket;
    }

    public S3CrashObjectStore(CrashStorageOptions options) : this(CreateClient(options), options) => _ownsClient = true;

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
        try
        {
            ListObjectsV2Response response = await _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                Delimiter = delimiter,
                ContinuationToken = continuationToken,
                MaxKeys = PageSize
            }, ct).ConfigureAwait(false);
            if ((response.S3Objects?.Count ?? 0) + (response.CommonPrefixes?.Count ?? 0) > PageSize
                || response.NextContinuationToken?.Length > 4096
                || response.IsTruncated == true && string.IsNullOrEmpty(response.NextContinuationToken))
            {
                throw new CrashStorageException(CrashFailureCode.StorageUnavailable);
            }
            StoredCrashObject[] objects = (response.S3Objects ?? []).Select(x => Fingerprint(x.Key, x.ETag, x.Size, x.LastModified)).ToArray();
            return new CrashObjectPage(objects, response.CommonPrefixes ?? [], response.IsTruncated == true ? response.NextContinuationToken : null);
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
        try
        {
            GetObjectMetadataResponse response = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = key }, ct).ConfigureAwait(false);
            return Fingerprint(key, response.ETag, response.ContentLength, response.LastModified);
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        long cap = Math.Min(maxBytes, CrashLimits.ArchiveBytes);
        if (expected.Size > cap)
            throw new CrashStorageException(CrashFailureCode.ArchiveTooLarge);
        if (expected.Size < 0)
            throw new CrashStorageException(CrashFailureCode.ObjectChanged);

        bool created = false;
        try
        {
            using GetObjectResponse response = await _client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucket,
                Key = expected.Key,
                EtagToMatch = expected.ETag
            }, ct).ConfigureAwait(false);
            if (Fingerprint(expected.Key, response.ETag, response.ContentLength, response.LastModified) != expected)
                throw new CrashStorageException(CrashFailureCode.ObjectChanged);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            created = true;
            byte[] buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                int read = await response.ResponseStream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, cap - total + 1)), ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                total += read;
                if (total > cap)
                    throw new CrashStorageException(CrashFailureCode.ArchiveTooLarge);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            if (total != expected.Size)
                throw new CrashStorageException(CrashFailureCode.ObjectChanged);
        }
        catch (Exception e) when (IsStorageError(e))
        {
            if (created)
                File.Delete(destination);
            throw Failure(e);
        }
        catch
        {
            if (created)
                File.Delete(destination);
            throw;
        }
    }

    private static StoredCrashObject Fingerprint(string key, string? etag, long? size, DateTime? modified)
    {
        if (string.IsNullOrEmpty(etag) || size is null or < 0 || modified is null)
            throw new CrashStorageException(CrashFailureCode.StorageUnavailable);
        return new StoredCrashObject(key, etag, size.Value, new DateTimeOffset(DateTime.SpecifyKind(modified.Value, DateTimeKind.Utc)));
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
