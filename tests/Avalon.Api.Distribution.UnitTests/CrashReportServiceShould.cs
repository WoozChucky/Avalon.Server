using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Distribution.UnitTests;

public class CrashReportServiceShould
{
    private readonly IDistributionStore _store = Substitute.For<IDistributionStore>();
    private readonly ICrashReportSlots _slots = Substitute.For<ICrashReportSlots>();

    [Theory]
    [InlineData("0.2.0", 0)]
    [InlineData("0.2.0", -1)]
    [InlineData("0.2.0", 4194305)]
    [InlineData("unknown", 1024)]
    [InlineData("../../escape", 1024)]
    public async Task Refuse_invalid_or_unpublished_reports_without_granting_storage(string version, long size)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CrashReportService(_store, _slots, cache, TimeProvider.System, Substitute.For<IHostApplicationLifetime>());

        Assert.Null(await service.CreateAsync(new CrashReportRequest(version, "123abcd", size), CancellationToken.None));
        _slots.DidNotReceiveWithAnyArgs().PresignCrash(default!, default, default);
    }

    [Fact]
    public async Task Accept_an_old_published_build_and_sign_its_exact_size_for_ten_minutes()
    {
        const string Key = "manifests/dev/0.2.0+1.123abcd.json";
        var doc = new ManifestDocument(1, "dev", "windows-x64", "0.2.0", "0.2.0+1.123abcd",
            DateTimeOffset.UtcNow.AddDays(-30), "0.1.0", "", 0, []);
        _store.ListManifestsAsync(Channel.Dev, Arg.Any<CancellationToken>()).Returns([new StoredObject(Key, doc.PublishedAt)]);
        _store.GetManifestAsync(Key, Arg.Any<CancellationToken>()).Returns(new StoredManifest(doc, "{}", "sig"));
        _slots.PresignCrash(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<TimeSpan>())
            .Returns(new Uri("https://storage.example/crashes/report.zip"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CrashReportService(_store, _slots, cache, TimeProvider.System, Substitute.For<IHostApplicationLifetime>());

        CrashReportSlot? slot = await service.CreateAsync(new CrashReportRequest("0.2.0", "123abcd", 4194304), CancellationToken.None);

        Assert.NotNull(slot);
        Assert.StartsWith("0.2.0/", slot.Key, StringComparison.Ordinal);
        Assert.EndsWith(".zip", slot.Key, StringComparison.Ordinal);
        _slots.Received(1).PresignCrash(slot.Key, 4194304, TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Finish_a_refresh_after_the_uploader_disconnects_and_reuse_unchanged_manifests()
    {
        const string Key = "manifests/dev/build.json";
        var doc = new ManifestDocument(1, "dev", "windows-x64", "0.2.0", "0.2.0+1.123abcd",
            DateTimeOffset.UtcNow.AddDays(-30), "0.1.0", "", 0, []);
        _store.ListManifestsAsync(Channel.Dev, Arg.Any<CancellationToken>()).Returns([new StoredObject(Key, doc.PublishedAt)]);
        using var disconnected = new CancellationTokenSource();
        _store.GetManifestAsync(Key, Arg.Any<CancellationToken>()).Returns(call =>
        {
            disconnected.Cancel();
            call.Arg<CancellationToken>().ThrowIfCancellationRequested();
            return Task.FromResult<StoredManifest?>(new StoredManifest(doc, "{}", "sig"));
        });
        _slots.PresignCrash(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<TimeSpan>())
            .Returns(new Uri("https://storage.example/crashes/report.zip"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CrashReportService(_store, _slots, cache, TimeProvider.System, Substitute.For<IHostApplicationLifetime>());
        var request = new CrashReportRequest("0.2.0", "123abcd", 1024);

        Assert.NotNull(await service.CreateAsync(request, disconnected.Token));
        cache.Remove("crash-known-versions");
        Assert.NotNull(await service.CreateAsync(request, CancellationToken.None));
        await _store.Received(1).GetManifestAsync(Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Bind_the_upload_size_and_type_in_the_signature_in_a_separate_private_bucket()
    {
        using var store = new S3DistributionStore(new DistributionConfiguration
        {
            Endpoint = "http://localhost:3900",
            PublicUrl = "https://storage.example",
            Region = "garage",
            Bucket = "avalon-dist",
            CrashBucket = "crashes",
            AccessKeyId = "test-key",
            SecretAccessKey = new string('a', 64),
        });

        Uri url = store.PresignCrash("0.2.0/2026-10-19/report.zip", 1024, TimeSpan.FromMinutes(10));

        Assert.Equal("/crashes/0.2.0/2026-10-19/report.zip", url.AbsolutePath);
        string query = Uri.UnescapeDataString(url.Query);
        Assert.Contains("X-Amz-SignedHeaders=content-length;content-type;host", query, StringComparison.Ordinal);
    }
}
