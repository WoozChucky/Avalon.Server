using System.ComponentModel.DataAnnotations;

namespace Avalon.Configuration;

/// <summary>The world server's send path (#875), under <c>Network</c>. Read once at startup.</summary>
public sealed class NetworkConfiguration
{
    public const string Section = "Network";

    /// <summary>
    /// Dedicated send threads, named <c>Network.Send.0</c> on; each connection belongs to one for its life. Default:
    /// about half the processors, 1 to 8.
    /// </summary>
    [Range(1, 64)]
    public int SendThreads { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    /// <summary>
    /// Bytes one connection may have queued or being written before it is closed as too slow
    /// (<c>DisconnectReason.SlowConnection</c>). Default 512 KiB.
    /// </summary>
    [Range(65_536, 67_108_864)]
    public int MaxPendingBytes { get; set; } = 512 * 1024;

    /// <summary>How long one write may stay pending before the connection is closed as stalled, without a notice. Default 10 s.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan MaxWriteStall { get; set; } = TimeSpan.FromSeconds(10);
}
