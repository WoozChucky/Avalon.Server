using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.World;
using Avalon.World.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Avalon.Server.World.UnitTests.Hosting;

/// <summary>
/// #543: the world host refuses to start, naming the setting, when one of its three databases or
/// the cache is not configured. The host is composed as its entry point composes it, over the
/// appsettings.json it ships with, and the refusal comes from <see cref="WorldStartup"/>, the
/// startup work Program runs between Build and Run, before its migrations, its seeding and its
/// cache connection: every database here points at a port nothing listens on, so a check that
/// came after them would fail with a connection error instead.
/// </summary>
public class WorldStartupValidationShould
{
    private const string Unreachable = "Host=127.0.0.1;Port=1;Timeout=1;Database=none;Username=none;Password=none";

    [Fact]
    public async Task Pass_startup_validation_with_the_shipped_settings()
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal));

        host.Services.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData("Database:Auth:ConnectionString", "Database:Auth:ConnectionString is required.")]
    [InlineData("Database:Characters:ConnectionString", "Database:Characters:ConnectionString is required.")]
    [InlineData("Database:World:ConnectionString", "Database:World:ConnectionString is required.")]
    [InlineData("Cache:Host", "'CacheConfiguration' members: 'Host'")]
    public async Task Refuse_to_start_without_a_required_setting_before_any_database_call(string setting, string named)
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            ["Database:Characters:ConnectionString"] = Unreachable,
            ["Database:World:ConnectionString"] = Unreachable,
            [setting] = " ",
        });

        OptionsValidationException refused = await Assert.ThrowsAsync<OptionsValidationException>(() => WorldStartup.PrepareAsync(host));

        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Game setting out of its range stops the start, naming the setting: a row at each bound with a reason. The
    /// floats must be finite (#532, #593, #526): the interest radius at least 1 m, the interest remove margin and both
    /// Fury rates 0 or more.
    /// </summary>
    [Theory]
    [InlineData("InterestRadius", "0.5")]
    [InlineData("InterestRadius", "NaN")]
    [InlineData("InterestRadius", "Infinity")]
    [InlineData("InterestRemoveMargin", "-0.5")]
    [InlineData("InterestRemoveMargin", "Infinity")]
    [InlineData("FuryFromDamageTaken", "-1")]
    [InlineData("FuryFromDamageTaken", "Infinity")]
    [InlineData("FuryDecayPerSecond", "-1")]
    [InlineData("FuryDecayPerSecond", "Infinity")]
    [InlineData("MaxPartySize", "1")]
    [InlineData("MaxPartySize", "41")]
    [InlineData("PartyInviteTimeoutSeconds", "0")]
    [InlineData("PartyLeaveGraceSeconds", "0")]
    [InlineData("PartyReturnRetrySeconds", "0")]
    [InlineData("PartyReturnRetrySeconds", "3601")]
    [InlineData("PartyExperienceModeCooldownSeconds", "-1")]
    [InlineData("PartyHealthPerExtraPlayer", "-0.1")]
    [InlineData("PartyEligibilityRange", "0.5")]
    [InlineData("PartyEligibilityRange", "Infinity")]
    [InlineData("PartyExperienceBonusPerExtra", "-1")]
    [InlineData("PartyExperienceBonusPerExtra", "10.5")]
    [InlineData("PartyExperienceLevelGap", "0")]
    [InlineData("MaxActiveQuests", "0")]
    [InlineData("MaxActiveQuests", "101")]
    [InlineData("MaxAurasPerUnit", "0")]
    [InlineData("MaxAurasPerUnit", "257")]
    [InlineData("MaxIgnoredCharacters", "0")]
    [InlineData("MaxIgnoredCharacters", "501")]
    public async Task Refuse_to_start_with_a_game_setting_out_of_range(string setting, string value)
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            ["Database:Characters:ConnectionString"] = Unreachable,
            ["Database:World:ConnectionString"] = Unreachable,
            ["Game:" + setting] = value,
        });

        OptionsValidationException refused = await Assert.ThrowsAsync<OptionsValidationException>(() => WorldStartup.PrepareAsync(host));

        Assert.Contains(setting, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>#593, #526: 0 turns the interest remove margin and either Fury rate off, and is allowed.</summary>
    [Fact]
    public async Task Accept_zero_for_the_settings_it_turns_off()
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Game:InterestRemoveMargin"] = "0",
            ["Game:FuryFromDamageTaken"] = "0",
            ["Game:FuryDecayPerSecond"] = "0",
        });

        host.Services.GetRequiredService<IStartupValidator>().Validate();

        GameConfiguration configuration = host.Services.GetRequiredService<IOptions<GameConfiguration>>().Value;
        Assert.Equal(0f, configuration.InterestRemoveMargin);
        Assert.Equal(0f, configuration.FuryFromDamageTaken);
        Assert.Equal(0f, configuration.FuryDecayPerSecond);
    }

    /// <summary>#593: Game:InterestRadius replaces Game:EffectBroadcastRadius outright, with no alias.</summary>
    [Fact]
    public async Task Bind_the_interest_radius_and_ignore_the_old_key()
    {
        using IHost renamed = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Game:InterestRadius"] = "42",
        });
        using IHost old = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Game:EffectBroadcastRadius"] = "42",
        });

        Assert.Equal(42f, renamed.Services.GetRequiredService<IOptions<GameConfiguration>>().Value.InterestRadius);
        Assert.Equal(60f, old.Services.GetRequiredService<IOptions<GameConfiguration>>().Value.InterestRadius);
    }

    [Theory]
    [InlineData("World:Admission:ApiUrl", "http://internal.example/")]
    [InlineData("World:Admission:ApiUrl", "https://internal.example/path")]
    [InlineData("World:Admission:ServerId", "")]
    [InlineData("World:Admission:ClientCertificatePath", "")]
    [InlineData("World:Admission:ApiCertificateSha256", "")]
    [InlineData("World:Admission:ApiCertificateSha256", "not-a-pin")]
    [InlineData("Hosting:Security:CertificatePath", "")]
    public async Task Refuse_to_start_without_authenticated_admission_configuration(string setting, string value)
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            [setting] = value,
        });
        await Assert.ThrowsAsync<OptionsValidationException>(() => WorldStartup.PrepareAsync(host));
    }

    private static async Task<IHost> BuildAsync(Dictionary<string, string?> overrides)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hosting:Security:CertificatePath"] = "mounted-world.pfx",
                ["World:Admission:ApiUrl"] = "https://internal.avalon.example/",
                ["World:Admission:ServerId"] = "world-one",
                ["World:Admission:ClientCertificatePath"] = "mounted-workload.pfx",
                ["World:Admission:ApiCertificateSha256"] = new string('A', 64)
            });
            builder.Configuration.AddInMemoryCollection(overrides);
            builder.Services
                .AddWorldServices()
                .AddSingleton<WorldServer>()
                .AddSingleton<IWorldServer>(provider => provider.GetRequiredService<WorldServer>())
                .AddHostedService(provider => provider.GetRequiredService<WorldServer>());
            return builder.Build();
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }
}
