using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Avalon.Api.Controllers;
using Avalon.Api.Previews;
using Avalon.Api.UnitTests.Authentication;
using Avalon.Api.Worlds;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using ItemRarity = Avalon.Domain.World.ItemRarity;
using ItemSlotType = Avalon.Domain.World.ItemSlotType;
using ItemSubClass = Avalon.Domain.World.ItemSubClass;
using StatType = Avalon.Domain.World.StatType;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.UnitTests.Worlds;

/// <summary>GET /public/preview/item/{id} and /ability/{id}: the HTML link-preview bots read.</summary>
public sealed partial class LinkPreviewShould : IAsyncLifetime
{
    private const ushort Open = 1;      // Player, the configured default
    private const ushort OtherOpen = 2; // Player
    private const ushort Staff = 3;     // Admin only

    private readonly IWorldRepository _authWorlds = Substitute.For<IWorldRepository>();
    // One repository per world, picked by the request's selected world, so a test can tell which world was read.
    private readonly Dictionary<ushort, IItemTemplateRepository> _items = new()
    {
        [Open] = Substitute.For<IItemTemplateRepository>(),
        [OtherOpen] = Substitute.For<IItemTemplateRepository>(),
        [Staff] = Substitute.For<IItemTemplateRepository>(),
    };
    private readonly IAbilityTemplateRepository _abilities = Substitute.For<IAbilityTemplateRepository>();
    private ApiAuthHost _host = null!;

    private static PreviewConfiguration Previews() => new()
    {
        SiteName = "Avalon",
        AbilityColour = "#BC8A4E",
        RarityColours = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Epic"] = "#C084FC",
            ["Rare"] = "#38BDF8",
            ["Legendary"] = "#FBBF24",
        },
    };

    public async Task InitializeAsync()
    {
        WorldDatabases databases = new(new[] { Open, OtherOpen, Staff }
            .Select(id => new ConfiguredWorld(new WorldId(id), $"Host=w{id}", $"Host=c{id}")));
        Row(Open, AccountAccessLevel.Player);
        Row(OtherOpen, AccountAccessLevel.Player);
        Row(Staff, AccountAccessLevel.Admin);

        Item(Open, new ItemTemplate
        {
            Id = new ItemTemplateId(14),
            Name = "Barkplate Helm",
            Rarity = ItemRarity.Epic,
            Slot = ItemSlotType.Head,
            SubClass = ItemSubClass.Helmet,
            RequiredLevel = 5,
            StatType1 = StatType.Armor,
            StatValue1 = 8,
            StatType2 = StatType.Strength,
            StatValue2 = 3,
            StatType3 = StatType.CritPct,
            StatValue3 = 2,
        });
        Item(OtherOpen, new ItemTemplate { Id = new ItemTemplateId(14), Name = "Other World Helm", Rarity = ItemRarity.Rare });
        Item(Staff, new ItemTemplate { Id = new ItemTemplateId(14), Name = "Staff Helm" });
        Item(Open, new ItemTemplate { Id = new ItemTemplateId(15), Name = "Tricky <script>\"&", Rarity = ItemRarity.Common });
        Item(Open, new ItemTemplate
        {
            Id = new ItemTemplateId(16),
            Name = "Wordy",
            Rarity = ItemRarity.Legendary,
            StatType1 = StatType.Strength,
            StatValue1 = 1,
            StatType2 = StatType.Agility,
            StatValue2 = 1,
            StatType3 = StatType.Intellect,
            StatValue3 = 1,
            StatType4 = StatType.Stamina,
            StatValue4 = 1,
            StatType5 = StatType.AttackDamage,
            StatValue5 = 1,
            StatType6 = StatType.AbilityDamage,
            StatValue6 = 1,
            StatType7 = StatType.AttackSpeed,
            StatValue7 = 1,
            StatType8 = StatType.MovementSpeed,
            StatValue8 = 1,
            StatType9 = StatType.Health,
            StatValue9 = 1,
            StatType10 = StatType.Power,
            StatValue10 = 1,
            DamageMin1 = 100000,
            DamageMax1 = 200000,
            DamageType1 = DamageType.Lightning,
            DamageMin2 = 100000,
            DamageMax2 = 200000,
            DamageType2 = DamageType.Poison,
        });

        _abilities.FindByIdAsync(new AbilityId(210), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AbilityTemplate
            {
                Id = new AbilityId(210),
                Name = "Cleave",
                Cost = 30,
                CostPowerType = PowerType.Mana,
                CastTime = 2500,
                Cooldown = 8000,
                AllowedClasses = [CharacterClass.Warrior],
                ScriptName = "ConeAbilityScript",
                EffectValue = 10,
                ScalingCoefficient = 0.5f,
                Effects = SpellEffect.Damage,
            });
        _abilities.FindByIdAsync(new AbilityId(211), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new AbilityTemplate { Id = new AbilityId(211), Name = "Strike <b>&\"" });

        _host = await Start("https://avalon.example/", Previews());
    }

    private Task<ApiAuthHost> Start(string? publicSite, PreviewConfiguration previews, ushort defaultWorld = Open) =>
        ApiAuthHost.StartAsync(configure: services =>
        {
            services.AddWorldDatabases(new WorldDatabases(new[] { Open, OtherOpen, Staff }
                .Select(id => new ConfiguredWorld(new WorldId(id), $"Host=w{id}", $"Host=c{id}"))));
            services.AddSingleton(_authWorlds);
            services.AddSingleton(new PublicWorldSettings(defaultWorld));
            services.AddSingleton(PublicSiteSettings.Create(publicSite));
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(previews));
            services.AddScoped(PerWorldItems);
            services.AddSingleton(_abilities);
        });

    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>
    /// The controller is built before it selects a world, so the repository forwards each call to the
    /// selected world's one, as the real one does by opening that world's context on use.
    /// </summary>
    private IItemTemplateRepository PerWorldItems(IServiceProvider sp)
    {
        ICurrentWorld current = sp.GetRequiredService<ICurrentWorld>();
        IItemTemplateRepository forwarding = Substitute.For<IItemTemplateRepository>();
        forwarding.FindByIdAsync(Arg.Any<ItemTemplateId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => _items[current.Id!.Value].FindByIdAsync(call.Arg<ItemTemplateId>(), call.Arg<bool>(),
                call.Arg<CancellationToken>()));
        return forwarding;
    }

    private void Row(ushort id, AccountAccessLevel required) =>
        _authWorlds.FindByIdAsync(Arg.Is<WorldId>(w => w.Value == id), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new WorldEntity
            {
                Id = new WorldId(id),
                Name = $"World{id}",
                AccessLevelRequired = required,
                Host = "h",
                MinVersion = "0.0.1",
                Version = "0.0.1",
            });

    private void Item(ushort world, ItemTemplate template) =>
        _items[world].FindByIdAsync(template.Id, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(template);

    private static string Meta(string html, string key)
    {
        Match m = MetaTag().Match(html.Replace("\r", ""));
        foreach (Match tag in MetaTag().Matches(html))
            if (tag.Groups[1].Value == key) return tag.Groups[2].Value;
        Assert.Fail($"no meta {key} in {html}");
        return "";
    }

    [GeneratedRegex("<meta (?:property|name)=\"([^\"]+)\" content=\"([^\"]*)\">")]
    private static partial Regex MetaTag();

    private static string Decoded(string html, string key) => WebUtility.HtmlDecode(Meta(html, key));

    private async Task<(HttpResponseMessage Response, string Html)> Get(string path)
    {
        HttpResponseMessage response = await _host.Client.GetAsync(path);
        return (response, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Describe_an_item_with_the_tags_a_bot_reads()
    {
        (HttpResponseMessage response, string html) = await Get("/public/preview/item/14");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Equal("Avalon", Meta(html, "og:site_name"));
        Assert.Equal("website", Meta(html, "og:type"));
        Assert.Equal("Barkplate Helm", Meta(html, "og:title"));
        Assert.Equal("https://avalon.example/item/14", Meta(html, "og:url"));
        Assert.Equal("#C084FC", Meta(html, "theme-color"));
        Assert.Equal("summary", Meta(html, "twitter:card"));
        Assert.Equal("Epic · Head · Helmet · 8 Armor · +3 Strength · +2% Crit · Requires Level 5",
            Decoded(html, "og:description"));
        Assert.Contains("<title>Barkplate Helm</title>", html, StringComparison.Ordinal);
        Assert.Contains("<body><a href=\"https://avalon.example/item/14\">Barkplate Helm</a></body>", html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromSeconds(300), response.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Describe_an_ability_with_the_tags_a_bot_reads()
    {
        (HttpResponseMessage response, string html) = await Get("/public/preview/ability/210");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cleave", Meta(html, "og:title"));
        Assert.Equal("Avalon", Meta(html, "og:site_name"));
        Assert.Equal("website", Meta(html, "og:type"));
        Assert.Equal("https://avalon.example/ability/210", Meta(html, "og:url"));
        Assert.Equal("#BC8A4E", Meta(html, "theme-color"));
        Assert.Equal("summary", Meta(html, "twitter:card"));
        Assert.Equal("Warrior · 30 Mana · 2.5 sec cast · 8 sec cooldown · 10 + 50% of Attack Damage",
            Decoded(html, "og:description"));
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromSeconds(300), response.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Use_the_default_world_when_none_is_named_and_leave_it_out_of_the_url()
    {
        (_, string html) = await Get("/public/preview/item/14");

        Assert.Equal("Barkplate Helm", Meta(html, "og:title"));
        Assert.DoesNotContain("world=", Meta(html, "og:url"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Echo_an_explicit_world_in_the_url()
    {
        (HttpResponseMessage response, string html) = await Get($"/public/preview/item/14?world={OtherOpen}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Other World Helm", Meta(html, "og:title"));
        Assert.Equal("https://avalon.example/item/14?world=2", Decoded(html, "og:url"));
        Assert.Contains($"<a href=\"https://avalon.example/item/14?world=2\">", html.Replace("&amp;", "&"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/public/preview/item/99")]
    [InlineData("/public/preview/ability/99")]
    public async Task Answer_404_with_a_not_found_page_for_a_missing_id(string path)
    {
        (HttpResponseMessage response, string html) = await Get(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<title>Not found · Avalon</title>", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/public/preview/item/14?world=3")]    // staff only
    [InlineData("/public/preview/item/14?world=9")]    // not configured
    [InlineData("/public/preview/item/14?world=01")]   // not canonical
    [InlineData("/public/preview/item/14?world=abc")]
    [InlineData("/public/preview/ability/210?world=3")]
    public async Task Answer_404_for_a_world_an_anonymous_caller_may_not_read(string path)
    {
        (HttpResponseMessage response, string html) = await Get(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Not found", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("Staff Helm", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serve_a_staff_world_privately_to_a_caller_who_may_enter_it()
    {
        Account admin = ApiAuthHost.MakeAccount(AccountAccessLevel.Admin);
        _host.AccountNowIs(admin);

        HttpResponseMessage response = await _host.GetAsync($"/public/preview/item/14?world={Staff}", ApiAuthHost.Mint(admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Staff Helm", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.False(response.Headers.CacheControl.Public);
    }

    [Fact]
    public async Task Encode_every_value_taken_from_game_data()
    {
        (_, string html) = await Get("/public/preview/item/15");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"&", html, StringComparison.Ordinal);
        Assert.Contains("Tricky &lt;script&gt;&quot;&amp;", html, StringComparison.Ordinal);
        Assert.Equal("Tricky <script>\"&", Decoded(html, "og:title"));
        Assert.Contains("<title>Tricky &lt;script&gt;&quot;&amp;</title>", html, StringComparison.Ordinal);

        (_, string ability) = await Get("/public/preview/ability/211");
        Assert.DoesNotContain("<b>", ability, StringComparison.Ordinal);
        Assert.Equal("Strike <b>&\"", Decoded(ability, "og:title"));
    }

    [Fact]
    public async Task Cap_an_overlong_description_on_one_line()
    {
        (_, string html) = await Get("/public/preview/item/16");

        string description = Decoded(html, "og:description");
        Assert.Equal(LinkPreviewText.MaxDescriptionLength, description.Length);
        Assert.EndsWith("…", description, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', description);
        Assert.StartsWith("Legendary", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Collapse_line_breaks_in_a_capped_text()
    {
        Assert.Equal("a b c", LinkPreviewText.Cap("a\r\nb\n  c"));
        Assert.Equal(200, LinkPreviewText.Cap(new string('x', 500)).Length);
    }

    [Fact]
    public async Task Fall_back_to_the_first_readable_world_when_the_default_is_not_readable()
    {
        await using ApiAuthHost host = await Start("https://avalon.example", Previews(), defaultWorld: Staff);

        string html = await host.Client.GetStringAsync("/public/preview/item/14");

        Assert.Equal("Barkplate Helm", Meta(html, "og:title"));
    }

    [Fact]
    public async Task Leave_og_url_out_and_link_relatively_when_no_public_site_is_configured()
    {
        await using ApiAuthHost host = await Start(null, Previews());

        string html = await host.Client.GetStringAsync("/public/preview/item/14");

        Assert.DoesNotContain("og:url", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http", html, StringComparison.Ordinal);
        Assert.Contains("<body><a href=\"/item/14\">Barkplate Helm</a></body>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Link_relatively_with_the_explicit_world_when_no_public_site_is_configured()
    {
        await using ApiAuthHost host = await Start("", Previews());

        string html = await host.Client.GetStringAsync($"/public/preview/ability/210?world={OtherOpen}");

        Assert.DoesNotContain("og:url", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"/ability/210?world=2\">Cleave</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Name_an_absolute_og_url_when_a_public_site_is_configured()
    {
        await using ApiAuthHost host = await Start("https://site.example/", Previews());

        string html = await host.Client.GetStringAsync("/public/preview/item/14");

        Assert.Equal("https://site.example/item/14", Meta(html, "og:url"));
    }

    [Fact]
    public async Task Use_a_configured_colour()
    {
        PreviewConfiguration previews = Previews();
        previews.RarityColours["epic"] = "#112233";
        previews.AbilityColour = "#AABBCC";
        await using ApiAuthHost host = await Start(null, previews);

        Assert.Equal("#112233", Meta(await host.Client.GetStringAsync("/public/preview/item/14"), "theme-color"));
        Assert.Equal("#AABBCC", Meta(await host.Client.GetStringAsync("/public/preview/ability/210"), "theme-color"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("purple")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("#C084FC; x")]
    public async Task Leave_theme_color_out_for_a_rarity_with_no_valid_colour(string? colour)
    {
        PreviewConfiguration previews = Previews();
        previews.RarityColours.Remove("Epic");
        if (colour is not null) previews.RarityColours["Epic"] = colour;
        previews.AbilityColour = colour;
        await using ApiAuthHost host = await Start(null, previews);

        HttpResponseMessage item = await host.Client.GetAsync("/public/preview/item/14");
        string itemHtml = await item.Content.ReadAsStringAsync();
        string abilityHtml = await host.Client.GetStringAsync("/public/preview/ability/210");

        Assert.Equal(HttpStatusCode.OK, item.StatusCode);
        Assert.DoesNotContain("theme-color", itemHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("theme-color", abilityHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Take_the_site_name_from_configuration()
    {
        PreviewConfiguration previews = Previews();
        previews.SiteName = "Realm <&>";
        await using ApiAuthHost host = await Start(null, previews);

        string html = await host.Client.GetStringAsync("/public/preview/item/14");
        HttpResponseMessage missing = await host.Client.GetAsync("/public/preview/item/99");

        Assert.Equal("Realm <&>", Decoded(html, "og:site_name"));
        Assert.Contains("<title>Not found · Realm <&></title>", WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leave_og_site_name_out_when_none_is_configured()
    {
        PreviewConfiguration previews = Previews();
        previews.SiteName = null;
        await using ApiAuthHost host = await Start(null, previews);

        string html = await host.Client.GetStringAsync("/public/preview/item/14");

        Assert.DoesNotContain("og:site_name", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://avalon.example/", "https://avalon.example")]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("  https://avalon.example/a/ ", "https://avalon.example/a")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalise_the_public_site_url(string? configured, string? expected) =>
        Assert.Equal(expected, PublicSiteSettings.Create(configured).Base);

    [Theory]
    [InlineData("avalon.example")]
    [InlineData("/item")]
    [InlineData("ftp://avalon.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://avalon.example/?a=1")]
    [InlineData("https://avalon.example/#x")]
    public void Refuse_a_public_site_url_that_is_not_an_absolute_http_url(string configured)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => PublicSiteSettings.Create(configured));

        Assert.Contains("Application:PublicSiteUrl", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Treat_an_invalid_token_as_anonymous()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/public/preview/item/14");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        Assert.Equal(HttpStatusCode.OK, (await _host.Client.SendAsync(request)).StatusCode);
    }
}
