using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Avalon.Crashes.Worker.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Storage;

public sealed class S3CrashObjectStoreShould
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly StoredCrashObject s_expected = new("0.1.0/2026-10-23/11111111111111111111111111111111.zip", "\"original\"", 3, s_now);

    [Fact]
    public async Task Bound_the_combined_sdk_wait_and_response_body_to_thirty_seconds()
    {
        var clock = new FakeTimeProvider(s_now);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GetObjectResponse response = Response([]);
        response.ResponseStream = new PartialResponseStream(async token =>
        {
            reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        });
        using var client = new Client
        {
            GetResult = response,
            BeforeCall = _ => { clock.Advance(TimeSpan.FromSeconds(25)); return Task.CompletedTask; }
        };
        using var caller = new CancellationTokenSource();
        var store = new S3CrashObjectStore(client, new CrashStorageOptions(), clock);
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Task download = store.DownloadAsync(s_expected, destination, CrashLimits.ArchiveBytes, caller.Token);
        try
        {
            await reading.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(File.Exists(destination));
            clock.Advance(TimeSpan.FromSeconds(4));
            Assert.False(download.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
            CrashStorageException error = await Assert.ThrowsAsync<CrashStorageException>(() => download.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(CrashFailureCode.StorageUnavailable, error.Code);
            Assert.False(File.Exists(destination));
        }
        finally
        {
            caller.Cancel();
            try { await download; } catch (Exception) { }
            File.Delete(destination);
        }
    }

    [Theory]
    [InlineData("list")]
    [InlineData("head")]
    [InlineData("get")]
    public async Task Bound_blocked_sdk_operations_to_thirty_seconds(string operation)
    {
        var clock = new FakeTimeProvider(s_now);
        using var client = new Client { BeforeCall = token => Task.Delay(Timeout.InfiniteTimeSpan, token) };
        using var caller = new CancellationTokenSource();
        var store = new S3CrashObjectStore(client, new CrashStorageOptions(), clock);
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Task pending = InvokeAsync(store, operation, destination, caller.Token);
        try
        {
            clock.Advance(TimeSpan.FromSeconds(29));
            Assert.False(pending.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(1));
            CrashStorageException error = await Assert.ThrowsAsync<CrashStorageException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(CrashFailureCode.StorageUnavailable, error.Code);
            Assert.False(File.Exists(destination));
        }
        finally
        {
            caller.Cancel();
            try { await pending; } catch (Exception) { }
            File.Delete(destination);
        }
    }

    [Theory]
    [InlineData("list")]
    [InlineData("head")]
    [InlineData("get")]
    [InlineData("body")]
    public async Task Preserve_earlier_caller_cancellation(string operation)
    {
        var clock = new FakeTimeProvider(s_now);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GetObjectResponse response = Response([]);
        response.ResponseStream = new PartialResponseStream(async token =>
        {
            reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        });
        using var client = new Client
        {
            GetResult = response,
            BeforeCall = operation == "body" ? null : token => Task.Delay(Timeout.InfiniteTimeSpan, token)
        };
        using var caller = new CancellationTokenSource();
        var store = new S3CrashObjectStore(client, new CrashStorageOptions(), clock);
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Task pending = InvokeAsync(store, operation, destination, caller.Token);
        try
        {
            if (operation == "body")
                await reading.Task.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromSeconds(10));
            caller.Cancel();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.False(File.Exists(destination));
        }
        finally
        {
            caller.Cancel();
            try { await pending; } catch (Exception) { }
            File.Delete(destination);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normalize_response_stream_transport_failures_and_delete_partial_files(bool httpFailure)
    {
        GetObjectResponse response = Response([]);
        response.ResponseStream = new PartialResponseStream(_ => ValueTask.FromException<int>(httpFailure
            ? new HttpIOException(HttpRequestError.ResponseEnded, "untrusted-path") : new IOException("untrusted-path")));
        using var client = new Client { GetResult = response };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            CrashStorageException error = await Assert.ThrowsAsync<CrashStorageException>(() => store.DownloadAsync(s_expected, destination, CrashLimits.ArchiveBytes, default));
            Assert.Equal(CrashFailureCode.StorageUnavailable, error.Code);
            Assert.DoesNotContain("untrusted-path", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(destination));
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task Keep_local_file_errors_distinct_and_preserve_existing_files()
    {
        using var client = new Client { GetResult = Response([1, 2, 3]) };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        string destination = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(destination, [9]);
            await Assert.ThrowsAnyAsync<IOException>(() => store.DownloadAsync(s_expected, destination, CrashLimits.ArchiveBytes, default));
            Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(destination));
        }
        finally { File.Delete(destination); }
    }

    private static Task InvokeAsync(S3CrashObjectStore store, string operation, string destination, CancellationToken ct) => operation switch
    {
        "list" => store.ListAsync("", null, null, ct),
        "head" => store.HeadAsync(s_expected.Key, ct),
        _ => store.DownloadAsync(s_expected, destination, CrashLimits.ArchiveBytes, ct)
    };

    private sealed class PartialResponseStream(Func<CancellationToken, ValueTask<int>> read) : MemoryStream
    {
        private bool _first = true;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_first)
                return read(cancellationToken);
            _first = false;
            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }
    }

    [Fact]
    public async Task Match_fractional_listing_time_to_http_head_and_get_precision()
    {
        using var client = new Client
        {
            ListResult = new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = s_expected.Key, ETag = s_expected.ETag, Size = 3, LastModified = s_now.AddMilliseconds(999).UtcDateTime }]
            },
            HeadResult = new GetObjectMetadataResponse { ETag = s_expected.ETag, LastModified = s_now.UtcDateTime, ContentLength = 3 },
            GetResult = Response([1, 2, 3])
        };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        StoredCrashObject discovered = Assert.Single((await store.ListAsync("", null, null, default)).Objects);
        Assert.Equal(s_now, discovered.LastModified);
        Assert.Equal(discovered, await store.HeadAsync(discovered.Key, default));
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await store.DownloadAsync(discovered, destination, CrashLimits.ArchiveBytes, default);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(destination));
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task Send_bounded_listing_parameters_and_keep_original_keys()
    {
        using var client = new Client
        {
            ListResult = new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = s_expected.Key, ETag = s_expected.ETag, Size = 3, LastModified = s_now.UtcDateTime }],
                CommonPrefixes = ["0.1.0/"],
                NextContinuationToken = "opaque",
                IsTruncated = true
            }
        };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        CrashObjectPage page = await store.ListAsync("0.1.0/", "/", "previous", default);
        Assert.Equal(s_expected, Assert.Single(page.Objects));
        Assert.Equal("opaque", page.NextContinuationToken);
        Assert.Equal("crashes", client.ListRequest!.BucketName);
        Assert.Equal("0.1.0/", client.ListRequest.Prefix);
        Assert.Equal("/", client.ListRequest.Delimiter);
        Assert.Equal("previous", client.ListRequest.ContinuationToken);
        Assert.InRange(client.ListRequest.MaxKeys!.Value, 1, 100);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, CrashFailureCode.ObjectMissing)]
    [InlineData(HttpStatusCode.PreconditionFailed, CrashFailureCode.ObjectChanged)]
    [InlineData(HttpStatusCode.Forbidden, CrashFailureCode.AccessDenied)]
    [InlineData(HttpStatusCode.ServiceUnavailable, CrashFailureCode.StorageUnavailable)]
    public async Task Map_download_errors_to_distinct_finite_outcomes(HttpStatusCode status, CrashFailureCode reason)
    {
        using var client = new Client { Error = new AmazonS3Exception("untrusted-key") { StatusCode = status } };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        CrashStorageException error = await Assert.ThrowsAsync<CrashStorageException>(() => store.DownloadAsync(s_expected, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), CrashLimits.ArchiveBytes, default));
        Assert.Equal(reason, error.Code);
        Assert.DoesNotContain("untrusted-key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Return_null_only_for_missing_head_and_preserve_fingerprint()
    {
        using var client = new Client { HeadResult = new GetObjectMetadataResponse { ETag = s_expected.ETag, LastModified = s_now.UtcDateTime, ContentLength = 3 } };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        Assert.Equal(s_expected, await store.HeadAsync(s_expected.Key, default));
        client.Error = new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound };
        Assert.Null(await store.HeadAsync(s_expected.Key, default));
        client.Error = new AmazonS3Exception("denied") { StatusCode = HttpStatusCode.Forbidden };
        Assert.Equal(CrashFailureCode.AccessDenied, (await Assert.ThrowsAsync<CrashStorageException>(() => store.HeadAsync(s_expected.Key, default))).Code);
    }

    [Fact]
    public async Task Reset_only_invalid_continuation_errors()
    {
        using var client = new Client { Error = new AmazonS3Exception("invalid") { StatusCode = HttpStatusCode.BadRequest, ErrorCode = "InvalidArgument" } };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        await Assert.ThrowsAsync<CrashContinuationTokenException>(() => store.ListAsync("", null, "stale", default));
        await Assert.ThrowsAsync<CrashStorageException>(() => store.ListAsync("", null, null, default));
    }

    [Fact]
    public async Task Stream_conditional_get_and_verify_the_entire_discovered_fingerprint()
    {
        using var client = new Client { GetResult = Response([1, 2, 3]) };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await store.DownloadAsync(s_expected, destination, CrashLimits.ArchiveBytes, default);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(destination));
            Assert.Equal(s_expected.ETag, client.GetRequest!.EtagToMatch);
            Assert.Equal(s_expected.Key, client.GetRequest.Key);
            Assert.Equal("crashes", client.GetRequest.BucketName);
        }
        finally { File.Delete(destination); }
    }

    [Theory]
    [InlineData("etag")]
    [InlineData("time")]
    [InlineData("declared")]
    [InlineData("truncated")]
    [InlineData("extra")]
    public async Task Reject_source_mutation_and_remove_partial_downloads(string mutation)
    {
        GetObjectResponse response = Response(mutation == "truncated" ? [1, 2] : mutation == "extra" ? [1, 2, 3, 4] : [1, 2, 3]);
        if (mutation == "etag") response.ETag = "changed";
        if (mutation == "time") response.LastModified = s_now.AddSeconds(1).UtcDateTime;
        if (mutation == "declared") response.ContentLength = 2;
        using var client = new Client { GetResult = response };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            CrashStorageException error = await Assert.ThrowsAsync<CrashStorageException>(() => store.DownloadAsync(s_expected, destination, CrashLimits.ArchiveBytes, default));
            Assert.Equal(CrashFailureCode.ObjectChanged, error.Code);
            Assert.False(File.Exists(destination));
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task Enforce_actual_stream_cap_even_when_content_length_lies()
    {
        GetObjectResponse response = Response(new byte[CrashLimits.ArchiveBytes + 1]);
        using var client = new Client { GetResult = response };
        var store = new S3CrashObjectStore(client, new CrashStorageOptions());
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            CrashStorageException error = await Assert.ThrowsAsync<CrashStorageException>(() => store.DownloadAsync(s_expected, destination, CrashLimits.ArchiveBytes, default));
            Assert.Equal(CrashFailureCode.ArchiveTooLarge, error.Code);
            Assert.False(File.Exists(destination));
        }
        finally { File.Delete(destination); }
    }

    private static GetObjectResponse Response(byte[] bytes) => new()
    {
        ETag = s_expected.ETag,
        ContentLength = s_expected.Size,
        LastModified = s_now.UtcDateTime,
        ResponseStream = new MemoryStream(bytes)
    };

    private sealed class Client() : AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config { ServiceURL = "http://127.0.0.1:1", ForcePathStyle = true })
    {
        public AmazonS3Exception? Error { get; set; }
        public Func<CancellationToken, Task>? BeforeCall { get; init; }
        public ListObjectsV2Response ListResult { get; init; } = new();
        public GetObjectMetadataResponse HeadResult { get; init; } = new();
        public GetObjectResponse GetResult { get; init; } = new();
        public ListObjectsV2Request? ListRequest { get; private set; }
        public GetObjectRequest? GetRequest { get; private set; }
        public override async Task<ListObjectsV2Response> ListObjectsV2Async(ListObjectsV2Request request, CancellationToken cancellationToken = default)
        {
            ListRequest = request;
            if (BeforeCall is not null)
                await BeforeCall(cancellationToken);
            return Error is null ? ListResult : throw Error;
        }
        public override async Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default)
        {
            if (BeforeCall is not null)
                await BeforeCall(cancellationToken);
            return Error is null ? HeadResult : throw Error;
        }
        public override async Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            GetRequest = request;
            if (BeforeCall is not null)
                await BeforeCall(cancellationToken);
            return Error is null ? GetResult : throw Error;
        }
    }
}
