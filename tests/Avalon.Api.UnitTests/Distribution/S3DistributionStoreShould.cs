using Avalon.Api.Distribution;
using Xunit;

namespace Avalon.Api.UnitTests.Distribution;

/// <summary>
/// The S3 (Garage) store: reads over the in-cluster endpoint, but presigns for the public host a
/// player's request will carry, because the signature covers the host.
/// </summary>
public class S3DistributionStoreShould
{
    private static DistributionConfiguration Config() => new()
    {
        Endpoint = "http://garage.distribution.svc:3900",
        PublicUrl = "https://dist.avalon.nunolevezinho.xyz",
        Bucket = "avalon-dist",
        Region = "garage",
        AccessKeyId = "GK0123456789abcdef01234567",
        SecretAccessKey = new string('a', 64),
    };

    [Fact]
    public void Presign_for_the_public_host_path_style_with_the_requested_lifetime()
    {
        using var store = new S3DistributionStore(Config());

        Uri url = store.Presign("blobs/sha256/ab12", TimeSpan.FromHours(1));

        Assert.Equal("https", url.Scheme);
        Assert.Equal("dist.avalon.nunolevezinho.xyz", url.Host);
        Assert.Equal("/avalon-dist/blobs/sha256/ab12", url.AbsolutePath);
        // The SDK derives X-Amz-Expires from "expiry minus now", so it may read 3599.
        var expires = int.Parse(System.Text.RegularExpressions.Regex.Match(url.Query, @"X-Amz-Expires=(\d+)").Groups[1].Value);
        Assert.InRange(expires, 3595, 3600);
        Assert.Contains("X-Amz-Signature=", url.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(DistributionConfiguration.Endpoint))]
    [InlineData(nameof(DistributionConfiguration.PublicUrl))]
    [InlineData(nameof(DistributionConfiguration.Bucket))]
    [InlineData(nameof(DistributionConfiguration.AccessKeyId))]
    [InlineData(nameof(DistributionConfiguration.SecretAccessKey))]
    public void Count_as_unconfigured_when_any_required_value_is_missing(string property)
    {
        DistributionConfiguration config = Config();
        typeof(DistributionConfiguration).GetProperty(property)!.SetValue(config, "");

        Assert.False(config.IsConfigured);
        Assert.True(Config().IsConfigured);
    }

    [Fact]
    public void Read_a_manifest_and_ignore_fields_it_does_not_know()
    {
        const string json = """
            { "schema": 1, "product": "avalon-client", "channel": "live", "platform": "windows-x64",
              "version": "0.2.0", "build": "0.2.0+142.1a2b3c4", "publishedAt": "2026-10-01T18:00:00Z",
              "minLauncherVersion": "1.0.0", "launch": { "exe": "runtime.exe", "args": [] },
              "notes": "hi", "totalSize": 12, "future": true,
              "files": [ { "path": "runtime.exe", "size": 12, "sha256": "ab" } ] }
            """;

        ManifestDocument doc = S3DistributionStore.ParseManifest(json);

        Assert.Equal("0.2.0+142.1a2b3c4", doc.Build);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 18, 0, 0, TimeSpan.Zero), doc.PublishedAt);
        Assert.Equal("ab", Assert.Single(doc.Files).Sha256);
    }

    [Fact]
    public void Refuse_a_manifest_schema_it_does_not_understand()
    {
        Assert.Throws<DistributionUnavailableException>(() =>
            S3DistributionStore.ParseManifest("""{ "schema": 2, "files": [] }"""));
    }

    [Fact]
    public void Read_the_launcher_release_from_taurus_format_plus_the_installer_block()
    {
        const string json = """
            { "version": "1.0.0", "notes": "first", "pub_date": "2026-10-01T18:00:00Z",
              "platforms": { "windows-x86_64": { "signature": "tsig", "url": "launcher/1.0.0/a.nsis.zip" } },
              "installer": { "key": "launcher/1.0.0/a-setup.exe", "size": 6502400, "sha256": "ee" } }
            """;

        LauncherRelease release = S3DistributionStore.ParseLauncher(json);

        Assert.Equal("tsig", release.SignatureWindows);
        Assert.Equal("launcher/1.0.0/a.nsis.zip", release.UpdateKey);
        Assert.Equal("launcher/1.0.0/a-setup.exe", release.InstallerKey);
        Assert.Equal(6502400, release.InstallerSize);
    }

    /// <summary>I3: a malformed object is "not available" (503), never an unhandled 500.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "version": "1.0.0" }""")]
    public void Treat_a_malformed_launcher_release_as_unavailable(string json)
    {
        Assert.Throws<DistributionUnavailableException>(() => S3DistributionStore.ParseLauncher(json));
    }

    [Fact]
    public void Treat_a_malformed_manifest_as_unavailable()
    {
        Assert.Throws<DistributionUnavailableException>(() => S3DistributionStore.ParseManifest("{ not json"));
    }

    /// <summary>
    /// Storage down is an outage (503), for a listing as for a read: the release feed lists a
    /// channel's manifests, and a refused connection there answered 500 (found live, Garage at 0).
    /// </summary>
    [Fact]
    public async Task Treat_storage_that_refuses_connections_as_unavailable_when_listing_or_reading()
    {
        DistributionConfiguration config = Config();
        config.Endpoint = "http://127.0.0.1:1";
        using var store = new S3DistributionStore(config);

        await Assert.ThrowsAsync<DistributionUnavailableException>(() =>
            store.ListManifestsAsync(Channel.Live, CancellationToken.None));
        await Assert.ThrowsAsync<DistributionUnavailableException>(() =>
            store.GetPointerAsync(Channel.Live, CancellationToken.None));
    }

    // ---- Changelog entries (homelab spec 2026-09-27-avalon-changelog-design §4) ----

    [Fact]
    public void Read_a_changelog_entry_without_its_commit()
    {
        const string json = """
            {"schema":1,"product":"server","channel":null,"version":"0.6.0","build":null,"commit":"35ab8b1f",
             "publishedAt":"2026-09-27T14:38:05Z","releaseUrl":"https://github.com/WoozChucky/Avalon.Server/releases/tag/v0.6.0",
             "items":[{"kind":"new","text":"Added browser sign-in to the launcher.","breaking":false,"pr":594,
                       "prUrl":"https://github.com/WoozChucky/Avalon.Server/pull/594"}]}
            """;

        ChangelogEntryDto? entry = S3DistributionStore.ParseChangelog(json);

        Assert.NotNull(entry);
        Assert.Equal("0.6.0", entry!.Version);
        Assert.Equal(594, Assert.Single(entry.Items).Pr);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 14, 38, 5, TimeSpan.Zero), entry.PublishedAt);
        Assert.Equal("https://github.com/WoozChucky/Avalon.Server/releases/tag/v0.6.0", entry.ReleaseUrl!.ToString());
    }

    [Fact]
    public void Read_a_private_repositorys_entry_without_links()
    {
        const string json = """
            {"schema":1,"product":"client","channel":"ptr","version":"0.1.0","build":"0.1.0+7.811af65","commit":"811af65",
             "publishedAt":"2026-09-27T19:44:32Z","releaseUrl":null,
             "items":[{"kind":"new","text":"Game client builds now list their changes as patch notes.","breaking":false}]}
            """;

        ChangelogEntryDto? entry = S3DistributionStore.ParseChangelog(json);

        Assert.Equal("ptr", entry!.Channel);
        Assert.Null(entry.ReleaseUrl);
        Assert.Null(Assert.Single(entry.Items).Pr);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"schema":2,"product":"server","version":"1","publishedAt":"2026-09-27T14:38:05Z","items":[]}""")]
    [InlineData("""{"schema":1,"product":"server"}""")]
    public void Skip_an_entry_it_cannot_read(string json)
    {
        Assert.Null(S3DistributionStore.ParseChangelog(json));
    }
}
