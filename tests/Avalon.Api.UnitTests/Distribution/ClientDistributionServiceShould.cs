using Avalon.Api.Distribution;
using Avalon.Common.Accounts;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Distribution;

/// <summary>
/// Game distribution (homelab spec 2026-09-27): which channels an account sees, and the presigned
/// links it gets. Channel access reuses the world rule (AccessLevels.ForWorld): access levels are
/// flags, so "PTR or above" cannot be an ordinal comparison.
/// </summary>
public class ClientDistributionServiceShould
{
    private readonly IDistributionStore _store = Substitute.For<IDistributionStore>();
    private readonly ClientDistributionService _service;
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    public ClientDistributionServiceShould()
    {
        _store.Presign(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(call => new Uri($"https://dist.example/avalon-dist/{call.Arg<string>()}?ttl={(int)call.Arg<TimeSpan>().TotalSeconds}"));
        _service = new ClientDistributionService(_store, new MemoryCache(new MemoryCacheOptions()));
    }

    private static ManifestDocument Doc(string channel, string build, DateTimeOffset published, params (string Path, string Sha)[] files) =>
        new(1, channel, "windows-x64", "0.2.0", build, published, "1.0.0", $"notes {build}",
            files.Sum(_ => 10L), files.Select(f => new ManifestFile(f.Path, 10, f.Sha)).ToList());

    private void Publish(Channel channel, ManifestDocument doc)
    {
        string key = $"manifests/{doc.Channel}/{doc.Build}.json";
        _store.GetPointerAsync(channel, Arg.Any<CancellationToken>()).Returns(new ChannelPointer(doc.Build, key));
        _store.GetManifestAsync(key, Arg.Any<CancellationToken>()).Returns(new StoredManifest(doc, $"{{\"build\":\"{doc.Build}\"}}", "sig-" + doc.Build));
        _store.ListManifestsAsync(channel, Arg.Any<CancellationToken>()).Returns(new List<StoredObject> { new(key, doc.PublishedAt) });
    }

    [Theory]
    [InlineData(AccountAccessLevel.Player, true, false, false)]
    [InlineData(AccountAccessLevel.PTR, true, true, false)]
    [InlineData(AccountAccessLevel.PTR | AccountAccessLevel.Player, true, true, false)]
    [InlineData(AccountAccessLevel.Tournament, true, false, false)]
    [InlineData(AccountAccessLevel.GameMaster, true, true, false)]
    [InlineData(AccountAccessLevel.Admin, true, true, true)]
    [InlineData(AccountAccessLevel.Console, true, true, true)]
    public void Gate_channels_like_the_worlds_they_mirror(AccountAccessLevel caller, bool live, bool ptr, bool dev)
    {
        Assert.Equal(live, ChannelAccess.Allows(Channel.Live, caller));
        Assert.Equal(ptr, ChannelAccess.Allows(Channel.Ptr, caller));
        Assert.Equal(dev, ChannelAccess.Allows(Channel.Dev, caller));
    }

    [Fact]
    public async Task Hide_a_channel_the_caller_may_not_use_without_presigning_anything()
    {
        Publish(Channel.Dev, Doc("dev", "b1", T0, ("runtime.exe", "aa")));

        Assert.Null(await _service.GetManifestAsync(Channel.Dev, AccountAccessLevel.Player, CancellationToken.None));
        _store.DidNotReceiveWithAnyArgs().Presign(default!, default);
    }

    [Fact]
    public async Task Presign_each_distinct_blob_once_for_an_hour_and_return_the_manifest_bytes_unchanged()
    {
        Publish(Channel.Live, Doc("live", "b1", T0, ("runtime.exe", "aa"), ("game.pak", "bb"), ("copy.pak", "bb")));

        ManifestResponse? res = await _service.GetManifestAsync(Channel.Live, AccountAccessLevel.Player, CancellationToken.None);

        Assert.NotNull(res);
        Assert.Equal("{\"build\":\"b1\"}", res!.Manifest);
        Assert.Equal("sig-b1", res.Signature);
        Assert.Equal(["aa", "bb"], res.Urls.Keys.Order());
        Assert.Equal("https://dist.example/avalon-dist/blobs/sha256/bb?ttl=3600", res.Urls["bb"].ToString());
        _store.Received(1).Presign("blobs/sha256/bb", TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Answer_unavailable_for_a_channel_whose_pointer_names_a_missing_manifest_but_keep_listing_the_others()
    {
        Publish(Channel.Live, Doc("live", "b1", T0, ("runtime.exe", "aa")));
        _store.GetPointerAsync(Channel.Ptr, Arg.Any<CancellationToken>()).Returns(new ChannelPointer("gone", "manifests/ptr/gone.json"));

        await Assert.ThrowsAsync<DistributionUnavailableException>(() =>
            _service.GetManifestAsync(Channel.Ptr, AccountAccessLevel.PTR, CancellationToken.None));

        IReadOnlyList<ChannelDto> channels = await _service.ListChannelsAsync(AccountAccessLevel.PTR, CancellationToken.None);
        Assert.Equal(["live"], channels.Select(c => c.Channel));
    }

    [Fact]
    public async Task List_only_live_releases_to_anonymous_callers_and_add_ptr_for_ptr_accounts()
    {
        Publish(Channel.Live, Doc("live", "b1", T0, ("runtime.exe", "aa")));
        Publish(Channel.Ptr, Doc("ptr", "b2", T0.AddDays(2), ("runtime.exe", "cc")));

        IReadOnlyList<ReleaseDto> anonymous = await _service.ListReleasesAsync(null, 10, CancellationToken.None);
        IReadOnlyList<ReleaseDto> ptr = await _service.ListReleasesAsync(AccountAccessLevel.PTR, 10, CancellationToken.None);

        Assert.Equal(["live"], anonymous.Select(r => r.Channel));
        Assert.Equal(["ptr", "live"], ptr.Select(r => r.Channel));
    }

    [Fact]
    public async Task Presign_the_installer_for_fifteen_minutes_and_the_update_bundle_for_an_hour()
    {
        _store.GetLauncherAsync(Arg.Any<CancellationToken>()).Returns(new LauncherRelease("1.0.0", "first", T0,
            "tauri-sig", "launcher/1.0.0/setup.nsis.zip", "launcher/1.0.0/setup.exe", 6502400, "ee"));

        LauncherDto? launcher = await _service.GetLauncherAsync(CancellationToken.None);
        TauriUpdateDto? update = await _service.GetLauncherUpdateAsync(CancellationToken.None);

        Assert.Equal("https://dist.example/avalon-dist/launcher/1.0.0/setup.exe?ttl=900", launcher!.Url.ToString());
        Assert.Equal(6502400, launcher.Size);
        Assert.Equal("https://dist.example/avalon-dist/launcher/1.0.0/setup.nsis.zip?ttl=3600",
            update!.Platforms["windows-x86_64"].Url.ToString());
        Assert.Equal("tauri-sig", update.Platforms["windows-x86_64"].Signature);
    }

    // ---- Final review fixes ----

    /// <summary>I1: a manifest caught between its upload and its .sig is not published yet, and a
    /// "not there" answer is never cached, so the release is visible as soon as it is complete.</summary>
    [Fact]
    public async Task Not_remember_a_missing_manifest_so_a_just_completed_release_is_served()
    {
        var doc = Doc("live", "b1", T0, ("runtime.exe", "aa"));
        _store.GetPointerAsync(Channel.Live, Arg.Any<CancellationToken>()).Returns(new ChannelPointer("b1", "manifests/live/b1.json"));
        _store.GetManifestAsync("manifests/live/b1.json", Arg.Any<CancellationToken>())
            .Returns((StoredManifest?)null, new StoredManifest(doc, "{}", "sig"));

        await Assert.ThrowsAsync<DistributionUnavailableException>(() =>
            _service.GetManifestAsync(Channel.Live, AccountAccessLevel.Player, CancellationToken.None));
        ManifestResponse? second = await _service.GetManifestAsync(Channel.Live, AccountAccessLevel.Player, CancellationToken.None);

        Assert.Equal("sig", second!.Signature);
    }

    /// <summary>I3: one channel's storage failure leaves the other channels listed.</summary>
    [Fact]
    public async Task Keep_listing_the_other_channels_when_one_channel_cannot_be_read()
    {
        Publish(Channel.Live, Doc("live", "b1", T0, ("runtime.exe", "aa")));
        _store.GetPointerAsync(Channel.Ptr, Arg.Any<CancellationToken>())
            .Returns<ChannelPointer?>(_ => throw new DistributionUnavailableException("ptr pointer unreadable"));

        IReadOnlyList<ChannelDto> channels = await _service.ListChannelsAsync(AccountAccessLevel.PTR, CancellationToken.None);

        Assert.Equal(["live"], channels.Select(c => c.Channel));
    }

    /// <summary>I4: anonymous endpoints do not reach storage on every request.</summary>
    [Fact]
    public async Task Cache_the_release_listing_and_the_launcher_release()
    {
        Publish(Channel.Live, Doc("live", "b1", T0, ("runtime.exe", "aa")));
        _store.GetLauncherAsync(Arg.Any<CancellationToken>()).Returns(new LauncherRelease("1.0.0", "", T0, "s", "u", "i", 1, "x"));

        for (int i = 0; i < 3; i++)
        {
            await _service.ListReleasesAsync(null, 10, CancellationToken.None);
            await _service.GetLauncherAsync(CancellationToken.None);
            await _service.GetLauncherUpdateAsync(CancellationToken.None);
        }

        await _store.Received(1).ListManifestsAsync(Channel.Live, Arg.Any<CancellationToken>());
        await _store.Received(1).GetLauncherAsync(Arg.Any<CancellationToken>());
    }
}
