using Microsoft.Extensions.Logging;

namespace Avalon.Balance.Core;

/// <summary>
/// A logger that keeps each Error a shared check logs, formatted, so a row the world would log and leave out stops the
/// run naming it. Lower levels are dropped.
/// </summary>
internal sealed class RefusalLog : ILogger
{
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors => _errors;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
            _errors.Add(formatter(state, exception));
    }
}
