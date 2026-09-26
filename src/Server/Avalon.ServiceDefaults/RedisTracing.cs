using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Instrumentation.StackExchangeRedis;
using StackExchange.Redis;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Hands Redis connections made outside DI to the Redis instrumentation (#540). The instrumentation
/// only exists once the tracer provider is built, and the servers connect before the host starts, so
/// a connection that arrives first waits here until the instrumentation attaches.
/// </summary>
public sealed class RedisTracing
{
    private readonly object _gate = new();
    private readonly List<IConnectionMultiplexer> _pending = [];
    private StackExchangeRedisInstrumentation? _instrumentation;

    /// <summary>How many connections have been handed to the instrumentation.</summary>
    public int Instrumented { get; private set; }

    internal void Attach(StackExchangeRedisInstrumentation instrumentation)
    {
        lock (_gate)
        {
            _instrumentation = instrumentation;
            foreach (IConnectionMultiplexer connection in _pending)
                Instrument(connection);
            _pending.Clear();
        }
    }

    public void Add(IConnectionMultiplexer connection)
    {
        lock (_gate)
        {
            if (_instrumentation is null)
                _pending.Add(connection);
            else
                Instrument(connection);
        }
    }

    private void Instrument(IConnectionMultiplexer connection)
    {
        _instrumentation!.AddConnection(connection);
        Instrumented++;
    }
}

public static class RedisTracingExtensions
{
    /// <summary>
    /// Traces the commands of a Redis connection the app made itself. A no-op where tracing is not
    /// configured.
    /// </summary>
    public static void TraceRedis(this IServiceProvider services, IConnectionMultiplexer connection) =>
        services.GetService<RedisTracing>()?.Add(connection);
}
