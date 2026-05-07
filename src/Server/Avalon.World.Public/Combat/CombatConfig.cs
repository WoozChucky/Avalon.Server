namespace Avalon.World.Public.Combat;

public sealed class CombatConfig
{
    public float    DefaultDecayRatePerSecond     { get; init; } = 1.0f;
    public float    OutOfRangeDecayMultiplier     { get; init; } = 3.0f;
    public float    EngagementRadius              { get; init; } = 60.0f;
    public int      MergeCapHostileParticipants   { get; init; } = 50;
    public uint     GcdMs                         { get; init; } = 200;
    public float    EncounterEndGraceSeconds      { get; init; } = 5.0f;
    public float    InitialThreatSeed             { get; init; } = 1.0f;
    public float    ReviveHealthFraction          { get; init; } = 0.25f;
    public uint     ThreatBroadcastIntervalMs     { get; init; } = 250;
    public float    ThreatBroadcastDeltaThreshold { get; init; } = 0.05f;
}
