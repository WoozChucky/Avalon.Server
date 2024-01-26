namespace Avalon.Metrics;

public class MetricsConfiguration
{
    public string ApiUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool Automatic { get; set; }
    public bool Enabled { get; set; }
    public bool Export { get; set; }
}
