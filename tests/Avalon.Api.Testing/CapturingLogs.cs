using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Avalon.Api.Testing;

/// <summary>A logger provider that keeps every entry, for tests that check what was logged.</summary>
public sealed class CapturingLogs : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyList<string> Warnings =>
        _entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();

    /// <summary>Every entry, its message and its exception's full text, if any.</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> All => _entries.ToList();

    public ILogger CreateLogger(string categoryName) => this;
    public void Dispose() { }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue((logLevel,
            exception is null ? formatter(state, exception) : formatter(state, exception) + "\n" + exception));
}
