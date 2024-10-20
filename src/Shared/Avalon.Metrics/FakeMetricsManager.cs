namespace Avalon.Metrics;

public class FakeMetricsManager : IMetricsManager
{
    public void Start(Dictionary<string, string>? defaultProperties = null)
    {

    }

    public void Stop()
    {

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

    }

    public void Dispose()
    {
        // TODO release managed resources here
    }
}
