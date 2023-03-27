using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Avalon.Server;

public class AvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly ConcurrentQueue<object> _messages;
    private readonly CancellationTokenSource _cts;

    public AvalonGame(ILogger<AvalonGame> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _messages = new ConcurrentQueue<object>();
        _cts = new CancellationTokenSource();
    }
    
    public void AddMessage(object message)
    {
        _messages.Enqueue(message);
    }
    
    public async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            if (_messages.TryDequeue(out var message))
            {
                // Process message
                _logger.LogInformation("Processing message: {Message}", message);
            }
        }
    }
}
