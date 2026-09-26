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

    /// <summary>
    /// The facing cone's half-angle, in degrees from the caster's facing (#513). A cast at a target
    /// is accepted only when the angle to the target is strictly less than this; exactly equal is
    /// refused as NotFacing. The client mirrors it from the wire: character select sends it on every
    /// AbilityInfo.FacingAngle, so changing it is a wire-visible change.
    /// </summary>
    public float    MaxFacingAngleDeg             { get; init; } = 65f;
}
