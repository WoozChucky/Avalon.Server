namespace Avalon.Network.Packets.Items;

/// <summary>How a sealed affix woke (#1180), as SItemAwakenedPacket tells it. Append-only.</summary>
public enum AwakeningOutcome
{
    /// <summary>The affix woke as it was sealed: the same tier and the same values.</summary>
    Woken = 0,

    /// <summary>The affix woke one tier better, its values rolled anew within that tier.</summary>
    TierRaised = 1,

    /// <summary>
    /// The affix woke at its own tier, each value rolled again within it and the higher of the two kept: the rise a wake
    /// gives when no better tier is within the item's reach.
    /// </summary>
    Polished = 2,
}
