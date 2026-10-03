namespace Avalon.Network.Packets.Auras;

/// <summary>What happened to one aura (auras). Append-only; Unknown is what a payload without the field reads as.</summary>
public enum AuraUpdateAction : byte
{
    Unknown = 0,
    Applied = 1,
    Refreshed = 2,
    Stacked = 3,
    Removed = 4,
}
