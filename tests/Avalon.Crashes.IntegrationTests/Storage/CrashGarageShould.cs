using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Avalon.Crashes.Worker.Storage;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Storage;

[Trait("Category", "CrashesGarage")]
public sealed class CrashGarageShould
{
    [IntegrationFact]
    public async Task Should_page_signed_objects_and_reject_a_changed_download_fingerprint()
    {
        static string Required(string name) => Environment.GetEnvironmentVariable("AVALON_CRASHES_TEST_S3_" + name)
            ?? throw new InvalidOperationException("Disposable Garage configuration required.");
        string endpoint = Required("ENDPOINT");
        string bucket = Required("BUCKET");
        Assert.Equal("https://garage-tls:8443", endpoint);
        Assert.Matches("^crashes-test-[0-9a-f]{32}$", bucket);
        var options = new CrashStorageOptions
        {
            Endpoint = endpoint,
            Bucket = bucket,
            AccessKeyId = Required("ACCESS_KEY"),
            SecretAccessKey = Required("SECRET_KEY")
        };
        using var client = new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = endpoint,
            AuthenticationRegion = "garage",
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        });
        using var store = new S3CrashObjectStore(options);
        string prefix = "1.0.0/2026-10-24/";
        for (int i = 0; i <= S3CrashObjectStore.PageSize; i++)
            await client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = prefix + i.ToString("x32") + ".zip", ContentBody = "synthetic archive" });
        CrashObjectPage first = await store.ListAsync(prefix, null, null, default);
        Assert.Equal(S3CrashObjectStore.PageSize, first.Objects.Count);
        Assert.NotNull(first.NextContinuationToken);
        CrashObjectPage second = await store.ListAsync(prefix, null, first.NextContinuationToken, default);
        Assert.Single(second.Objects);
        Assert.Null(second.NextContinuationToken);
        Assert.Equal(101, first.Objects.Concat(second.Objects).Select(o => o.Key).Distinct().Count());
        StoredCrashObject source = first.Objects[0];
        Assert.Equal(source, await store.HeadAsync(source.Key, default));
        Assert.Null(await store.HeadAsync(prefix + "missing.zip", default));
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await store.DownloadAsync(source, destination, CrashLimits.ArchiveBytes, default);
            Assert.Equal("synthetic archive", await File.ReadAllTextAsync(destination));
            File.Delete(destination);
            await client.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = source.Key, ContentBody = "changed synthetic archive" });
            CrashStorageException error = await Assert.ThrowsAsync<CrashStorageException>(() => store.DownloadAsync(source, destination, CrashLimits.ArchiveBytes, default));
            Assert.Equal(CrashFailureCode.ObjectChanged, error.Code);
            Assert.False(File.Exists(destination));
        }
        finally { File.Delete(destination); }
    }
}
