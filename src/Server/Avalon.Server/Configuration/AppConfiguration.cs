using Avalon.Infrastructure.Configuration;
using Avalon.Metrics;

namespace Avalon.Server.Configuration;

public class AppConfiguration
{
    public NetworkDaemonConfiguration NetworkDaemon { get; set; }
    public MetricsConfiguration Metrics { get; set; }
}
