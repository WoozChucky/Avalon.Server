using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.World;
using Avalon.World.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

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

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(() => WorldStartup.PrepareAsync(host));

        Assert.Contains(named, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TcpKeepAliveTimeSeconds", "0")]
    [InlineData("TcpKeepAliveIntervalSeconds", "-1")]
    [InlineData("TcpKeepAliveRetryCount", "0")]
    [InlineData("TcpKeepAliveTimeSeconds", "32768")]
    [InlineData("TcpKeepAliveIntervalSeconds", "32768")]
    [InlineData("TcpKeepAliveRetryCount", "128")]
    public async Task Refuse_to_start_with_a_tcp_keepalive_setting_out_of_range(string setting, string value)
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            ["Database:Characters:ConnectionString"] = Unreachable,
            ["Database:World:ConnectionString"] = Unreachable,
            ["Hosting:" + setting] = value,
        });

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(() => WorldStartup.PrepareAsync(host));

        Assert.Contains(setting, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>#532, #593: the interest radius must be finite and at least 1 m.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("0.5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task Refuse_to_start_with_an_interest_radius_out_of_range(string value)
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            ["Database:Characters:ConnectionString"] = Unreachable,
            ["Database:World:ConnectionString"] = Unreachable,
            ["Game:InterestRadius"] = value,
        });

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(() => WorldStartup.PrepareAsync(host));

        Assert.Contains("InterestRadius", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>#593: the interest remove margin must be finite and 0 or more.</summary>
    [Theory]
    [InlineData("-0.5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task Refuse_to_start_with_an_interest_remove_margin_out_of_range(string value)
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Database:Auth:ConnectionString"] = Unreachable,
            ["Database:Characters:ConnectionString"] = Unreachable,
            ["Database:World:ConnectionString"] = Unreachable,
            ["Game:InterestRemoveMargin"] = value,
        });

        var refused = await Assert.ThrowsAsync<OptionsValidationException>(() => WorldStartup.PrepareAsync(host));

        Assert.Contains("InterestRemoveMargin", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>#593: a margin of 0 turns the margin off and is allowed.</summary>
    [Fact]
    public async Task Accept_an_interest_remove_margin_of_zero()
    {
        using IHost host = await BuildAsync(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Game:InterestRemoveMargin"] = "0",
        });

        host.Services.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal(0f, host.Services.GetRequiredService<IOptions<GameConfiguration>>().Value.InterestRemoveMargin);
    }

    [Fact]
    public void Default_the_interest_range_to_60_and_10()
    {
        var configuration = new GameConfiguration();
        Assert.Equal(60f, configuration.InterestRadius);
        Assert.Equal(10f, configuration.InterestRemoveMargin);
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

    private static async Task<IHost> BuildAsync(Dictionary<string, string?> overrides)
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
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
