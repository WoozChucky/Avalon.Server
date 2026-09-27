using System.Security.Claims;
using System.Text.Json;
using Avalon.Api.Controllers;
using Avalon.Api.Distribution;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Controllers;

public class ClientDistributionControllerShould
{
    private readonly IDistributionStore _store = Substitute.For<IDistributionStore>();
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    public ClientDistributionControllerShould()
    {
        _store.Presign(Arg.Any<string>(), Arg.Any<TimeSpan>()).Returns(c => new Uri($"https://dist.example/{c.Arg<string>()}"));
    }

    private ClientDistributionController Sut(ClaimsPrincipal? user = null) =>
        new(new ClientDistributionService(_store, new MemoryCache(new MemoryCacheOptions())))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) },
            },
        };

    private static ClaimsPrincipal Account(params string[] levels) =>
        new(new ClaimsIdentity(levels.Select(l => new Claim(ClaimTypes.GroupSid, l)), "Bearer"));

    private void Publish(Channel channel, string build)
    {
        string key = $"manifests/{channel.Wire()}/{build}.json";
        var doc = new ManifestDocument(1, channel.Wire(), "windows-x64", "0.2.0", build, T0, "1.0.0", "notes", 10,
            [new ManifestFile("runtime.exe", 10, "aa")]);
        _store.GetPointerAsync(channel, Arg.Any<CancellationToken>()).Returns(new ChannelPointer(build, key));
        _store.GetManifestAsync(key, Arg.Any<CancellationToken>()).Returns(new StoredManifest(doc, "{}", "sig"));
        _store.ListManifestsAsync(channel, Arg.Any<CancellationToken>()).Returns(new List<StoredObject> { new(key, T0) });
    }

    [Fact]
    public async Task List_only_live_releases_to_an_anonymous_visitor()
    {
        Publish(Channel.Live, "b1");
        Publish(Channel.Ptr, "b2");

        var result = Assert.IsType<OkObjectResult>(await Sut().Releases(10, CancellationToken.None));

        Assert.Equal(["live"], ((IReadOnlyList<ReleaseDto>)result.Value!).Select(r => r.Channel));
    }

    [Fact]
    public async Task Add_the_channels_a_signed_in_account_may_use_to_the_releases()
    {
        Publish(Channel.Live, "b1");
        Publish(Channel.Ptr, "b2");

        var result = Assert.IsType<OkObjectResult>(await Sut(Account("Player", "PTR")).Releases(10, CancellationToken.None));

        Assert.Contains("ptr", ((IReadOnlyList<ReleaseDto>)result.Value!).Select(r => r.Channel));
    }

    [Theory]
    [InlineData("dev")]      // exists, hidden from a Player
    [InlineData("nightly")]  // does not exist
    public async Task Answer_not_found_alike_for_a_hidden_and_an_unknown_channel(string channel)
    {
        Publish(Channel.Dev, "b3");

        Assert.IsType<NotFoundResult>(await Sut(Account("Player")).Manifest(channel, CancellationToken.None));
    }

    [Fact]
    public async Task Return_the_manifest_with_its_blob_urls()
    {
        Publish(Channel.Live, "b1");

        var result = Assert.IsType<OkObjectResult>(await Sut(Account("Player")).Manifest("live", CancellationToken.None));
        var body = Assert.IsType<ManifestResponse>(result.Value);

        Assert.Equal("sig", body.Signature);
        Assert.Equal("https://dist.example/blobs/sha256/aa", body.Urls["aa"].ToString());
    }

    [Fact]
    public async Task Answer_not_found_when_no_launcher_is_published()
    {
        _store.GetLauncherAsync(Arg.Any<CancellationToken>()).Returns((LauncherRelease?)null);

        Assert.IsType<NotFoundResult>(await Sut().Launcher(CancellationToken.None));
    }

    [Fact]
    public void Serialise_the_update_response_in_taurus_shape()
    {
        var dto = new TauriUpdateDto("1.0.0", "first", T0,
            new Dictionary<string, TauriPlatformDto> { ["windows-x86_64"] = new("tsig", new Uri("https://dist.example/u.zip")) });

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.True(json.RootElement.TryGetProperty("pub_date", out _));
        Assert.Equal("tsig", json.RootElement.GetProperty("platforms").GetProperty("windows-x86_64").GetProperty("signature").GetString());
    }
}
