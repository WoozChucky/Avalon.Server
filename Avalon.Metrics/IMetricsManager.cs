using System.Collections.Concurrent;
using QuixStreams.Streaming;

namespace Avalon.Metrics;

public interface IMetricsManager : IDisposable
{
    Task OpenAsync();
    Task SendEventAsync(string eventName, string eventValue, Dictionary<string, string>? properties = null);
    Task SendMetricsAsync(string metricName, string metricValue, Dictionary<string, string>? properties = null);
    Task SendMetricsAsync(string metricName, bool metricValue, Dictionary<string, string>? properties = null);
    Task SendMetricsAsync(string metricName, byte[] metricValue, Dictionary<string, string>? properties = null);
    void SetDefaultProperties(Dictionary<string, string> properties);
}

public class MetricsManager : IMetricsManager
{
    
    private ConcurrentDictionary<string, string> _defaultProperties;
    
    private QuixStreamingClient _streamingClient;
    private ITopicProducer _topicProducer;
    private IStreamProducer _streamProducer;

    public MetricsManager()
    {
        
    }

    public Task OpenAsync(Dictionary<string, string>? defaultProperties = null)
    {
        
        
        return Task.CompletedTask;
    }

    public Task SendEventAsync(string eventName, string eventValue, Dictionary<string, string>? properties = null)
    {
        throw new NotImplementedException();
    }

    public Task SendMetricsAsync(string metricName, string metricValue, Dictionary<string, string>? properties = null)
    {
        throw new NotImplementedException();
    }

    public Task SendMetricsAsync(string metricName, bool metricValue, Dictionary<string, string>? properties = null)
    {
        throw new NotImplementedException();
    }

    public Task SendMetricsAsync(string metricName, byte[] metricValue, Dictionary<string, string>? properties = null)
    {
        throw new NotImplementedException();
    }

    public void SetDefaultProperties(Dictionary<string, string> properties)
    {
        throw new NotImplementedException();
    }
}
