using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Avalon.Metrics;

public interface IMetricsManager : IDisposable
{
    void Start(Dictionary<string, string>? defaultProperties = null);
    void Stop();

    void QueueEvent(string eventName, string eventValue, Dictionary<string, string>? properties = null);
    void QueueMetric(string metricName, string metricValue, Dictionary<string, string>? properties = null);
    void QueueMetric(string metricName, double metricValue, Dictionary<string, string>? properties = null);
    void QueueMetric(string metricName, byte[] metricValue, Dictionary<string, string>? properties = null);
    void SetDefaultProperties(Dictionary<string, string> properties);
}

public class MetricsManager : IMetricsManager
{

    private ConcurrentDictionary<string, string>? _defaultProperties;

    private readonly CancellationTokenSource _cancellationTokenSource;

    private volatile bool _running;

    private readonly ILogger<MetricsManager> _logger;

    public MetricsManager(ILogger<MetricsManager> logger, MetricsConfiguration configuration)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
        _running = false;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public void Start(Dictionary<string, string>? defaultProperties = null)
    {
        if (_running) throw new InvalidOperationException("Metrics Manager is already running");

        _running = true;

        _defaultProperties = new ConcurrentDictionary<string, string>(defaultProperties ?? new Dictionary<string, string>());

        _logger.LogInformation("Metrics Manager is starting");

        Task.Factory.StartNew(Worker, _cancellationTokenSource.Token);
    }

    public void Stop()
    {
        if (!_running) throw new InvalidOperationException("Metrics Manager is not running");

        _logger.LogInformation("Metrics Manager is stopping");

        _running = false;
        _cancellationTokenSource.Cancel();
    }

    public void QueueEvent(string eventName, string eventValue, Dictionary<string, string>? properties = null)
    {
    }

    public void QueueMetric(string metricName, string metricValue, Dictionary<string, string>? properties = null)
    {
    }

    public void QueueMetric(string metricName, double metricValue, Dictionary<string, string>? properties = null)
    {
    }

    public void QueueMetric(string metricName, byte[] metricValue, Dictionary<string, string>? properties = null)
    {
    }

    public void SetDefaultProperties(Dictionary<string, string> properties)
    {
        _defaultProperties = new ConcurrentDictionary<string, string>(properties);
    }

    private async Task Worker()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Metrics Manager Worker Task cancelled");
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Metrics Manager Worker Task encountered an error");
            }
            finally
            {
                await Task.Delay(10);
            }
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }
}
