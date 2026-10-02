using Avalon.Hosting;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Maintenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Server.World.UnitTests.Hosting;

/// <summary>
/// World:Shutdown (#768): how long a stopping world warns and drains its players, and the margin the close and the
/// saves get after it. The host's stop timeout is their sum, or the host abandons the stop mid-countdown.
/// </summary>
public class WorldShutdownOptionsShould
{
    [Fact]
    public void Bind_the_drain_and_set_the_host_stop_timeout_to_the_drain_plus_the_margin()
    {
        using ServiceProvider provider = Build(("World:Shutdown:DrainTime", "00:05:00"),
            ("World:Shutdown:SaveMargin", "00:01:00"));

        WorldShutdownConfiguration shutdown = provider.GetRequiredService<IOptions<WorldShutdownConfiguration>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(5), shutdown.DrainTime);
        Assert.Equal(TimeSpan.FromMinutes(1), shutdown.SaveMargin);
        Assert.Equal(TimeSpan.FromMinutes(6), provider.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
    }

    [Theory]
    [InlineData("-00:00:01", "00:01:00")]
    [InlineData("02:00:00", "00:01:00")]
    [InlineData("00:05:00", "00:00:00")]
    [InlineData("00:05:00", "00:00:20")]
    [InlineData("00:05:00", null)]
    public void Refuse_a_drain_or_margin_out_of_range(string drain, string? margin)
    {
        using ServiceProvider provider = Build(("World:Shutdown:DrainTime", drain),
            ("World:Shutdown:SaveMargin", margin));

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<WorldShutdownConfiguration>>().Value);
        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<HostOptions>>().Value);
    }

    /// <summary>
    /// The margin covers the shutdown's own wait for saves and the drain's backstop past its deadline: any less, and a
    /// drain that runs to its backstop leaves the save wait no time at all.
    /// </summary>
    [Fact]
    public void Require_a_margin_of_at_least_the_save_wait_and_the_drain_backstop()
    {
        Assert.Equal(WorldServer.DefaultSaveDrainLimit + WorldMaintenanceCoordinator.RestartTickBackstop,
            WorldShutdownConfiguration.MinimumSaveMargin);

        using ServiceProvider provider = Build(("World:Shutdown:DrainTime", "00:05:00"),
            ("World:Shutdown:SaveMargin", WorldShutdownConfiguration.MinimumSaveMargin.ToString()));
        Assert.Equal(WorldShutdownConfiguration.MinimumSaveMargin,
            provider.GetRequiredService<IOptions<WorldShutdownConfiguration>>().Value.SaveMargin);
    }

    /// <summary>The shipped appsettings.json drains nothing and keeps the host's 30 s stop timeout: today's stop.</summary>
    [Fact]
    public async Task Ship_no_drain_and_the_default_stop_timeout()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.World);
            builder.Services.AddWorldServices();
            using IHost host = builder.Build();

            Assert.Equal(TimeSpan.Zero,
                host.Services.GetRequiredService<IOptions<WorldShutdownConfiguration>>().Value.DrainTime);
            Assert.Equal(TimeSpan.FromSeconds(30),
                host.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    /// <summary>The host builder is shared: only the world host takes the drain into its stop timeout.</summary>
    [Fact]
    public async Task Leave_the_other_hosts_stop_timeout_alone()
    {
        string workingDirectory = Directory.GetCurrentDirectory();
        try
        {
            HostApplicationBuilder builder = await AvalonHostBuilder.CreateHostAsync([], ComponentType.Auth);
            builder.Configuration.AddInMemoryCollection([
                new KeyValuePair<string, string?>("World:Shutdown:DrainTime", "00:05:00"),
                new KeyValuePair<string, string?>("World:Shutdown:SaveMargin", "00:01:00"),
            ]);
            using IHost host = builder.Build();

            Assert.Equal(TimeSpan.FromSeconds(30),
                host.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
        }
        finally
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
    }

    private static ServiceProvider Build(params (string Key, string? Value)[] settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Where(setting => setting.Value is not null)
                .Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddWorldShutdown();
        return services.BuildServiceProvider();
    }
}
