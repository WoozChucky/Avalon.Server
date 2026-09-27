using System;
using System.Net.Sockets;
using System.Threading;
using Avalon.Configuration;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Networking;

/// <summary>
/// TCP keepalive on every socket the server accepts (#571), so the OS closes a half-open connection
/// (the peer gone without a FIN or RST) within about time + interval × retries, and the close path
/// runs. The values come from <see cref="HostingConfiguration" />, validated at startup.
/// </summary>
public sealed class TcpKeepAlive
{
    private readonly ILogger _logger;
    private int _warned;

    public TcpKeepAlive(HostingConfiguration configuration, ILogger logger)
    {
        _logger = logger;
        TimeSeconds = configuration.TcpKeepAliveTimeSeconds;
        IntervalSeconds = configuration.TcpKeepAliveIntervalSeconds;
        RetryCount = configuration.TcpKeepAliveRetryCount;
    }

    public int TimeSeconds { get; }
    public int IntervalSeconds { get; }
    public int RetryCount { get; }

    /// <summary>
    /// Turns keepalive on and sets its three values. An option the platform refuses is skipped, not
    /// fatal: the connection is still served, and the first refusal is logged once at Warning.
    /// </summary>
    public void Apply(Socket socket)
    {
        Set(socket, SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 1);
        Set(socket, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, TimeSeconds);
        Set(socket, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, IntervalSeconds);
        Set(socket, SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, RetryCount);
    }

    private void Set(Socket socket, SocketOptionLevel level, SocketOptionName option, int value)
    {
        try
        {
            socket.SetSocketOption(level, option, value);
        }
        catch (Exception e) when (e is SocketException or PlatformNotSupportedException)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
            {
                _logger.LogWarning(e,
                    "TCP keepalive option {Option} could not be set on this platform; half-open connections may stay open longer. Logged once",
                    option);
            }
        }
    }
}
