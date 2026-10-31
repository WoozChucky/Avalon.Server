using Avalon.Balance.Core;
using Avalon.Balance.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avalon.Balance.Service.UnitTests;

internal static class BalanceTestHost
{
    public const string Secret = "0123456789abcdef0123456789abcdef-secret";

    /// <summary>On TestServer. <paramref name="extra" /> adds configuration, e.g. <c>Balance:RunWorker</c>=false to pause the worker.</summary>
    public static WebApplication Build(string? secret = Secret, IReadOnlyDictionary<string, string?>? extra = null,
        Action<IServiceCollection>? services = null)
    {
        WebApplicationBuilder builder = Create(secret, extra);
        builder.WebHost.UseTestServer();
        return BalanceServiceHost.Build(builder, services);
    }

    /// <summary>The service's host over the seed and the checked-in files (#1183 Task 11 removes it).</summary>
    public static BalanceHost FixtureHost()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "balance");
        return new BalanceHost(SeedSource.Load(), new BalanceConfig(
            ConfigFileStore.Load(Path.Combine(dir, "scenarios.json"), ConfigFiles.ParseScenarios),
            ConfigFileStore.Load(Path.Combine(dir, "targets.json"), ConfigFiles.ParseTargets),
            ConfigFileStore.Load(Path.Combine(dir, "builds.json"), ConfigFiles.ParseBuilds)));
    }

    /// <summary>Registers <see cref="FixtureHost" /> over the service's own host.</summary>
    public static void UseFixtureRotations(IServiceCollection services) => services.AddSingleton(FixtureHost());

    /// <summary>On the real Kestrel, on a free loopback port: the only way to see Kestrel's own limits.</summary>
    public static WebApplication BuildKestrel(string? secret = Secret)
    {
        WebApplicationBuilder builder = Create(secret, null);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return BalanceServiceHost.Build(builder);
    }

    private static WebApplicationBuilder Create(string? secret, IReadOnlyDictionary<string, string?>? extra)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production });
        if (secret is not null)
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Balance:SharedSecret"] = secret });
        if (extra is not null)
            builder.Configuration.AddInMemoryCollection(extra);
        return builder;
    }
}
