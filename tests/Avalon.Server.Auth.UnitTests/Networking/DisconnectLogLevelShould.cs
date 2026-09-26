using Avalon.Hosting.Networking;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Avalon.Server.Auth.UnitTests.Networking;

/// <summary>
/// The Kubernetes TCP probes connect every few seconds and send nothing (#528); their
/// disconnects are Debug. A connection that said anything is worth an Information line.
/// </summary>
public class DisconnectLogLevelShould
{
    [Theory]
    [InlineData(0, LogLevel.Debug)]
    [InlineData(1, LogLevel.Information)]
    [InlineData(500, LogLevel.Information)]
    public void Log_a_disconnect_at_debug_only_when_the_connection_sent_nothing(int packets, LogLevel level)
    {
        Assert.Equal(level, Connection.DisconnectLogLevel(packets));
    }
}
