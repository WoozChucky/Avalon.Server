using System.ComponentModel.DataAnnotations;

namespace Avalon.Configuration;

public class HostingConfiguration
{
    public string Host { get; set; } = string.Empty;
    public ushort Port { get; set; }

    /// <summary>
    /// Internal read-buffer size in bytes used by <see cref="PacketReader"/>.
    /// Valid range: 512–65535. Defaults to 4096.
    /// </summary>
    [Range(512, 65535)]
    public int PacketReaderBufferSize { get; set; } = 4096;

    /// <summary>
    /// Per-connection outbound packet buffer capacity. Packets beyond this limit are dropped (oldest first).
    /// Valid range: 10–10000. Defaults to 100.
    /// </summary>
    [Range(10, 10000)]
    public int SendBufferCapacity { get; set; } = 100;

    /// <summary>
    /// TCP keepalive on every accepted socket (#571): seconds a connection may sit idle before the
    /// first probe. With the interval and retry count below, the OS closes a half-open connection
    /// (the peer gone without a FIN or RST) and the close path runs. Valid range: 1–32767.
    /// </summary>
    [Range(1, 32767)]
    public int TcpKeepAliveTimeSeconds { get; set; } = 60;

    /// <summary>Seconds between keepalive probes that go unanswered (#571). Valid range: 1–32767.</summary>
    [Range(1, 32767)]
    public int TcpKeepAliveIntervalSeconds { get; set; } = 10;

    /// <summary>Unanswered keepalive probes before the OS closes the connection (#571). Valid range: 1–127.</summary>
    [Range(1, 127)]
    public int TcpKeepAliveRetryCount { get; set; } = 3;

    /// <summary>PROXY protocol v2 from a fronting L4 proxy. Disabled unless configured.</summary>
    public ProxyProtocolConfiguration ProxyProtocol { get; set; } = new();

    public TelemetryConfiguration Telemetry { get; set; } = new();

}
