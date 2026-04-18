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
    /// When true, each <see cref="Connection"/> logs an inbound-packet-count snapshot
    /// (grouped by <c>NetworkPacketType</c>) every <see cref="PacketTypeRateLogIntervalSeconds"/> seconds.
    /// Counting is always on (≈100 ns / packet); only the periodic log is gated.
    /// </summary>
    public bool LogPacketTypeRates { get; set; } = false;

    /// <summary>
    /// Interval (seconds) between per-type packet snapshot logs. Only used when
    /// <see cref="LogPacketTypeRates"/> is true. Valid range: 1–600. Defaults to 10.
    /// </summary>
    [Range(1, 600)]
    public int PacketTypeRateLogIntervalSeconds { get; set; } = 10;
}
