using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.UnitTests;

/// <summary>A logger, and a factory handing out that same logger, that records every entry written to it.</summary>
internal sealed class TestLog : ILogger, ILoggerFactory
{
    public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

    public IEnumerable<(LogLevel Level, Exception? Exception, string Message)> Errors =>
        Entries.Where(e => e.Level == LogLevel.Error);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception, formatter(state, exception)));

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider) { }

    public void Dispose() { }
}
