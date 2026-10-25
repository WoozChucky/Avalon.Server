using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Avalon.Crashes.Worker.Symbols;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Symbols;

public sealed class S3CrashSymbolStoreShould
{
    [Fact]
    public async Task Should_honor_downward_configured_total_download_limit()
    {
        using var client = new Client { Bytes = new byte[4] };
        using var store = new S3CrashSymbolStore(client, new() { DownloadBytes = 3 });
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<CrashSymbolException>(() => store.DownloadOriginalAsync(SymbolIdentityValidatorShould.s_runtime, false, path, 8, new(), default));
            Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Should_preserve_original_key_case_and_only_upload_to_converted_bucket()
    {
        using var client = new Client();
        using var store = new S3CrashSymbolStore(client, new());
        ModuleIdentity identity = SymbolIdentityValidatorShould.s_runtime with { Name = "Runtime.EXE", PdbName = "Runtime.PDB" };
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await store.DownloadOriginalAsync(identity, false, destination, 8, new(), default);
            Assert.Equal("symbols", client.GetRequest!.BucketName);
            Assert.Equal("Runtime.EXE/123456781000/Runtime.EXE", client.GetRequest.Key);
            File.Delete(destination);
            await store.DownloadOriginalAsync(identity, true, destination, 8, new(), default);
            Assert.Equal("Runtime.PDB/00112233445566778899AABBCCDDEEFF1/Runtime.PDB", client.GetRequest!.Key);
            byte[] bytes = Encoding.UTF8.GetBytes(SymbolCacheShould.Content(identity));
            await File.WriteAllBytesAsync(destination, bytes);
            var metadata = new SymbolCacheMetadata(identity.Name, identity.ImageKey, identity.PdbName!, identity.PdbKey!, new CrashSymbolOptions().CacheVersion,
                CrashSymbolOptions.ConverterVersion, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
            await store.UploadConvertedAsync(identity, metadata, destination, default);
            Assert.Equal("symbols-sym", client.PutRequest!.BucketName);
            Assert.Equal($"{metadata.CacheVersion}/Runtime.PDB/00112233445566778899AABBCCDDEEFF1/Runtime.sym", client.PutRequest.Key);
        }
        finally { File.Delete(destination); }
    }

    [Fact]
    public async Task Should_count_all_downloads_and_archive_against_one_budget_and_delete_partial_files()
    {
        using var client = new Client { Bytes = new byte[4] };
        using var store = new S3CrashSymbolStore(client, new());
        var budget = new SymbolDownloadBudget(10);
        budget.Charge(3); // Already downloaded archive.
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await store.DownloadOriginalAsync(SymbolIdentityValidatorShould.s_runtime, false, destination, 4, budget, default);
            Assert.Equal(7, budget.UsedBytes);
            File.Delete(destination);
            CrashSymbolException error = await Assert.ThrowsAsync<CrashSymbolException>(() => store.DownloadOriginalAsync(SymbolIdentityValidatorShould.s_runtime, true, destination, 4, budget, default));
            Assert.Equal(CrashFailureCode.ResourceLimit, error.Code);
            Assert.False(File.Exists(destination));
            client.Bytes = new byte[5];
            client.DeclaredLength = 4;
            await Assert.ThrowsAsync<CrashSymbolException>(() => store.DownloadOriginalAsync(SymbolIdentityValidatorShould.s_runtime, true, destination, 4, new(), default));
            Assert.False(File.Exists(destination));
        }
        finally { File.Delete(destination); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_bound_sdk_wait_and_body_together_and_preserve_caller_cancellation(bool callerCancels)
    {
        var clock = new FakeTimeProvider();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new Client { BeforeCall = () => clock.Advance(TimeSpan.FromSeconds(25)), Body = new WaitingStream(reading) };
        using var store = new S3CrashSymbolStore(client, new(), clock);
        using var caller = new CancellationTokenSource();
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Task download = store.DownloadOriginalAsync(SymbolIdentityValidatorShould.s_runtime, false, path, 8, new(), caller.Token);
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(download.IsCompleted);
        if (callerCancels)
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download.WaitAsync(TimeSpan.FromSeconds(3)));
        }
        else
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            CrashSymbolException error = await Assert.ThrowsAsync<CrashSymbolException>(() => download.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(CrashFailureCode.StorageUnavailable, error.Code);
        }
        Assert.False(File.Exists(path));
    }

    internal sealed class WaitingStream(TaskCompletionSource reading) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            reading.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Client() : AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config { ServiceURL = "http://127.0.0.1:1" })
    {
        public byte[] Bytes { get; set; } = new byte[1];
        public long? DeclaredLength { get; set; }
        public Stream? Body { get; set; }
        public Action? BeforeCall { get; set; }
        public GetObjectRequest? GetRequest { get; private set; }
        public PutObjectRequest? PutRequest { get; private set; }
        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            GetRequest = request;
            BeforeCall?.Invoke();
            return Task.FromResult(new GetObjectResponse { ContentLength = DeclaredLength ?? Bytes.Length, ResponseStream = Body ?? new MemoryStream(Bytes) });
        }
        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            PutRequest = request;
            return Task.FromResult(new PutObjectResponse());
        }
    }
}
