using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public class AvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly ConcurrentQueue<object> _messages;

    public AvalonGame(ILogger<AvalonGame> logger, CancellationTokenSource cts)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = cts ?? throw new ArgumentNullException(nameof(cts));
        _messages = new ConcurrentQueue<object>();
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
