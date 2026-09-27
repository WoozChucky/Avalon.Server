using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.World;
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
