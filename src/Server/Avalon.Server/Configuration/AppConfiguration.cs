using Avalon.Database.Configuration;
using Avalon.Infrastructure.Configuration;
using Avalon.Metrics;

namespace Avalon.Server.Configuration;

public class AppConfiguration
{
    public InfrastructureConfiguration Infrastructure { get; set; }
    public NetworkDaemonConfiguration NetworkDaemon { get; set; }
    public MetricsConfiguration Metrics { get; set; }
    public DatabaseConfiguration Database { get; set; }
}
