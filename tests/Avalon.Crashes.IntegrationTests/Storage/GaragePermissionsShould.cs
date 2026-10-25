using System.Net;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Avalon.Crashes.Worker.Storage;
using Avalon.Crashes.Worker.Symbols;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Storage;

[Trait("Category", "CrashesGarage")]
public sealed class GaragePermissionsShould
{
    [IntegrationFact]
    public async Task Should_round_trip_converted_cache_through_the_production_adapter()
    {
        var options = new CrashSymbolOptions();
        var identity = new ModuleIdentity("runtime.exe", "123456781000", "runtime.pdb", "00112233445566778899AABBCCDDEEFF1", null, 0, 4096);
        string input = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string output = input + ".read";
        byte[] bytes = "MODULE windows x86_64 00112233445566778899AABBCCDDEEFF1 runtime.pdb\nFUNC 100 20 0 Synthetic\nSTACK CFI INIT 100 20 .cfa: $rsp 8 + .ra: .cfa 8 - ^\n"u8.ToArray();
        var metadata = new SymbolCacheMetadata(identity.Name, identity.ImageKey, identity.PdbName!, identity.PdbKey!, options.CacheVersion,
            CrashSymbolOptions.ConverterVersion, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        using var store = new S3CrashSymbolStore(new CrashStorageOptions
        {
            Endpoint = GarageClients.Required("ENDPOINT"),
            AccessKeyId = GarageClients.Required("WORKER_ACCESS_KEY"),
            SecretAccessKey = GarageClients.Required("WORKER_SECRET_KEY")
        }, options);
        try
        {
            await File.WriteAllBytesAsync(input, bytes);
            await store.UploadConvertedAsync(identity, metadata, input, default);
            Assert.Equal(metadata, await store.DownloadConvertedAsync(identity, options.CacheVersion, output, bytes.Length, new(), default));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(output));
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
            using AmazonS3Client admin = GarageClients.Create();
            await admin.DeleteObjectAsync("symbols-sym", $"{options.CacheVersion}/{identity.PdbName}/{identity.PdbKey}/runtime.sym");
        }
    }

    [IntegrationFact]
    public async Task Should_allow_only_scoped_bucket_and_database_operations()
    {
        using AmazonS3Client admin = GarageClients.Create();
        using AmazonS3Client worker = GarageClients.Create("WORKER_");
        using AmazonS3Client anonymous = GarageClients.Create(anonymous: true);
        string key = "permission-canary-" + Guid.NewGuid().ToString("N");
        foreach (string bucket in new[] { "crashes", "symbols" })
        {
            await admin.PutObjectAsync(new() { BucketName = bucket, Key = key, ContentBody = "synthetic permission probe" });
            using GetObjectResponse read = await worker.GetObjectAsync(bucket, key);
            Assert.Equal(HttpStatusCode.OK, read.HttpStatusCode);
            AmazonS3Exception denied = await Assert.ThrowsAsync<AmazonS3Exception>(() => worker.PutObjectAsync(new()
            { BucketName = bucket, Key = key, ContentBody = "forbidden overwrite" }));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            AmazonS3Exception deleteDenied = await Assert.ThrowsAsync<AmazonS3Exception>(() => worker.DeleteObjectAsync(bucket, key));
            Assert.Equal(HttpStatusCode.Forbidden, deleteDenied.StatusCode);
            AmazonS3Exception unsigned = await Assert.ThrowsAsync<AmazonS3Exception>(() => anonymous.GetObjectAsync(bucket, key));
            Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
            await admin.DeleteObjectAsync(bucket, key);
        }
        await worker.PutObjectAsync(new() { BucketName = "symbols-sym", Key = key, ContentBody = "synthetic converted cache" });
        using GetObjectResponse cached = await worker.GetObjectAsync("symbols-sym", key);
        Assert.Equal(HttpStatusCode.OK, cached.HttpStatusCode);
        AmazonS3Exception anonymousCache = await Assert.ThrowsAsync<AmazonS3Exception>(() => anonymous.GetObjectAsync("symbols-sym", key));
        Assert.Equal(HttpStatusCode.Forbidden, anonymousCache.StatusCode);
        await admin.DeleteObjectAsync("symbols-sym", key);

        await using var database = new VerticalDatabase();
        await database.InitializeAsync();
        await database.AssertRestrictedRoleAsync();
    }
}

internal static class GarageClients
{
    public static string Required(string suffix) => Environment.GetEnvironmentVariable("AVALON_CRASHES_TEST_S3_" + suffix)
        ?? throw new InvalidOperationException("Generated Garage configuration required.");

    public static AmazonS3Client Create(string prefix = "", bool anonymous = false) => new(
        anonymous ? new AnonymousAWSCredentials() : new BasicAWSCredentials(Required(prefix + "ACCESS_KEY"), Required(prefix + "SECRET_KEY")),
        new AmazonS3Config
        {
            ServiceURL = Required("ENDPOINT"),
            AuthenticationRegion = "garage",
            ForcePathStyle = true,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        });
}
