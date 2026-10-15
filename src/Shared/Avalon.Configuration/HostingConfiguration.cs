using System.ComponentModel.DataAnnotations;

namespace Avalon.Configuration;

public class HostingConfiguration
{
    public string Host { get; set; } = string.Empty;
    public ushort Port { get; set; }

    /// <summary>
    /// Internal read-buffer size in bytes used by <c>PacketReader</c>.
    /// Valid range: 512–65535. Defaults to 4096.
    /// </summary>
    [Range(512, 65535)]
    public int PacketReaderBufferSize { get; set; } = 4096;

    /// <summary>The default of <see cref="MaxInboundFrameSize" />.</summary>
    public const int DefaultMaxInboundFrameSize = 16_384;

    /// <summary>
    /// The most bytes a frame a peer sends may declare after its length prefix (its header and payload). A frame that
    /// declares more, or whose length prefix is malformed, is never buffered: the connection is closed, on either
    /// server, before admission or login as after. Frames the servers send are not limited by it.
    /// Valid range: 1024–16777216. Defaults to 16384.
    /// </summary>
    [Range(1024, 16_777_216)]
    public int MaxInboundFrameSize { get; set; } = DefaultMaxInboundFrameSize;

    /// <summary>The default of <see cref="InboundFrameTimeoutSeconds" />.</summary>
    public const int DefaultInboundFrameTimeoutSeconds = 10;

    /// <summary>
    /// Seconds a peer has to complete a frame once its first byte has arrived (#938). A frame still incomplete after
    /// them closes the connection, on either server, before admission or login as after. A connection idle between
    /// frames is not timed by it. There is no off: the largest frame a client sends is under a kilobyte.
    /// Valid range: 1–300. Defaults to 10.
    /// </summary>
    [Range(1, 300)]
    public int InboundFrameTimeoutSeconds { get; set; } = DefaultInboundFrameTimeoutSeconds;

    /// <summary>
    /// The auth server's per-connection outbound queue (<c>ChannelOutbox</c>): packets beyond it are dropped, oldest
    /// first. The world server does not read it: its connections send through the send threads (<c>Network</c>, #875).
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
