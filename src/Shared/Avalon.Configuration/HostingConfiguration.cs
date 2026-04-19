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

}
