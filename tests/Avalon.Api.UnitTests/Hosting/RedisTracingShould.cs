using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using Xunit;

namespace Avalon.Api.UnitTests.Hosting;

/// <summary>
/// ReplicatedCache makes its own Redis connection, so the Redis instrumentation, which only looks in
/// DI, never saw it and no Redis command appeared in any trace (#540). The hosts hand the connection
/// over with TraceRedis, and it must reach the instrumentation whichever is ready first: the servers
/// connect before the host starts, which is before the tracer provider is built.
/// </summary>
public class RedisTracingShould
{
    // A port nothing listens on: the multiplexer is created without a server behind it.
    private static Task<ConnectionMultiplexer> Unconnected() =>
        ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
        {
            EndPoints = { "127.0.0.1:1" },
            AbortOnConnectFail = false,
            ConnectTimeout = 100,
        });

    private static IHost Host()
    {
        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(new());
        builder.ConfigureOpenTelemetry();
        return builder.Build();
    }

    [Fact]
    public async Task Hand_over_a_connection_made_before_the_tracer_provider_is_built()
    {
        using IHost host = Host();
        await using ConnectionMultiplexer redis = await Unconnected();

        host.Services.TraceRedis(redis);
        _ = host.Services.GetRequiredService<TracerProvider>();

        Assert.Equal(1, host.Services.GetRequiredService<RedisTracing>().Instrumented);
    }

    [Fact]
    public async Task Hand_over_a_connection_made_after_the_tracer_provider_is_built()
    {
        using IHost host = Host();
        _ = host.Services.GetRequiredService<TracerProvider>();
        await using ConnectionMultiplexer redis = await Unconnected();

        host.Services.TraceRedis(redis);

        Assert.Equal(1, host.Services.GetRequiredService<RedisTracing>().Instrumented);
    }

    [Fact]
    public async Task Do_nothing_where_tracing_is_not_configured()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        await using ConnectionMultiplexer redis = await Unconnected();

        services.TraceRedis(redis);
    }
}
