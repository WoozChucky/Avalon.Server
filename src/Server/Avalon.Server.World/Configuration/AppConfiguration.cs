using Avalon.Configuration;
using Avalon.Infrastructure.Configuration;
using Avalon.Metrics;
using Avalon.World.Configuration;

namespace Avalon.Server.World.Configuration;

public class AppConfiguration
{
    public InfrastructureConfiguration? Infrastructure { get; set; }
    public NetworkDaemonConfiguration? NetworkDaemon { get; set; }
    public MetricsConfiguration? Metrics { get; set; }
    public DatabaseConfiguration? Database { get; set; }
    public GameConfiguration? Game { get; set; }
    public CacheConfiguration? Cache { get; set; }
}
