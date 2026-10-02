namespace Avalon.World.Auras;

/// <summary>What happened to an aura, as a client is told. The numbers are the wire's AuraUpdateAction's.</summary>
public enum AuraChangeKind
{
    Applied = 1,
    Refreshed = 2,
    Stacked = 3,
    Removed = 4,
}
