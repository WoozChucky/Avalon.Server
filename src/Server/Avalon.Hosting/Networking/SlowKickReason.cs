namespace Avalon.Hosting.Networking;

/// <summary>Why a connection was closed as too slow (#875), as <c>network.out.slow_kicks</c> tags it.</summary>
public enum SlowKickReason
{
    /// <summary>More than <c>Network:MaxPendingBytes</c> queued or being written.</summary>
    Bytes = 1,

    /// <summary>One write pending longer than <c>Network:MaxWriteStall</c>.</summary>
    Stall = 2,
}
