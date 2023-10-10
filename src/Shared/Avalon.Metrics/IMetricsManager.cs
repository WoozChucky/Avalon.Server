using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QuixStreams.Streaming;
using QuixStreams.Streaming.Models;
using QuixStreams.Telemetry.Models;

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
    
    private readonly ConcurrentQueue<EventData> _unprocessedEvents;
    private readonly ConcurrentQueue<TimeseriesData> _unprocessedMetrics;
    
    private readonly CancellationTokenSource _cancellationTokenSource;
    
    private volatile bool _running;
    
    private readonly ILogger<MetricsManager> _logger;
    
    private readonly QuixStreamingClient _streamingClient;
    private ITopicProducer? _topicProducer;
    private IStreamProducer? _streamProducer;

    public MetricsManager(ILogger<MetricsManager> logger, MetricsConfiguration configuration)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        _logger = logger;
        _running = false;
        _unprocessedEvents = new ConcurrentQueue<EventData>();
        _unprocessedMetrics = new ConcurrentQueue<TimeseriesData>();
        _streamingClient = new QuixStreamingClient(configuration.ApiKey, configuration.Automatic)
        {
            ApiUrl = new Uri(configuration.ApiUrl)
        };
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public void Start(Dictionary<string, string>? defaultProperties = null)
    {
        if (_running) throw new InvalidOperationException("Metrics Manager is already running");
        
        _running = true;
        
        Environment.SetEnvironmentVariable("Quix__Workspace__Id", "quixdev-nuno-dev");
        
        _topicProducer = _streamingClient.GetTopicProducer("av");
        _streamProducer = _topicProducer.GetOrCreateStream("mystream");
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
        var eventData = new EventData(eventName, DateTime.UtcNow, eventValue);
        eventData.AddTags(properties != null ? properties : _defaultProperties);
        
        _unprocessedEvents.Enqueue(eventData);
    }

    public void QueueMetric(string metricName, string metricValue, Dictionary<string, string>? properties = null)
    {
        var metricData = new TimeseriesData();
        metricData.AddTimestamp(DateTime.UtcNow)
            .AddValue(metricName, metricValue)
            .AddTags(properties != null ? properties : _defaultProperties);
        
        _unprocessedMetrics.Enqueue(metricData);
    }

    public void QueueMetric(string metricName, double metricValue, Dictionary<string, string>? properties = null)
    {
        var metricData = new TimeseriesData();
        metricData.AddTimestamp(DateTime.UtcNow)
            .AddValue(metricName, metricValue)
            .AddTags(properties != null ? properties : _defaultProperties);
        
        _unprocessedMetrics.Enqueue(metricData);
    }

    public void QueueMetric(string metricName, byte[] metricValue, Dictionary<string, string>? properties = null)
    {
        var metricData = new TimeseriesData();
        metricData.AddTimestamp(DateTime.UtcNow)
            .AddValue(metricName, metricValue)
            .AddTags(properties != null ? properties : _defaultProperties);
        
        _unprocessedMetrics.Enqueue(metricData);
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
                if (!_unprocessedEvents.IsEmpty)
                {
                    while (_unprocessedEvents.TryDequeue(out var evt))
                    {
                        _streamProducer?.Events.Publish(evt);
                    }
                }

                if (!_unprocessedMetrics.IsEmpty)
                {
                    while (_unprocessedMetrics.TryDequeue(out var metric))
                    {
                        _streamProducer?.Timeseries.Publish(metric);
                    }
                }
            }
            catch (OperationCanceledException e)
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
        _topicProducer?.Dispose();
        _streamProducer?.Close(StreamEndType.Aborted);
        _streamProducer?.Dispose();
        _cancellationTokenSource.Dispose();
    }
}
