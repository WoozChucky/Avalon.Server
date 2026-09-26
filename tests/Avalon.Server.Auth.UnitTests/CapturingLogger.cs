using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.Auth.UnitTests;

/// <summary>An <see cref="ILogger"/> that keeps every entry, for tests that assert what was logged and at which level.</summary>
internal sealed class CapturingLogger : ILogger
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue((logLevel, formatter(state, exception)));

    public int Count(LogLevel level) => Entries.Count(e => e.Level == level);
}
