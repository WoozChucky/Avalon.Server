using System.ComponentModel.DataAnnotations;

namespace Avalon.Configuration;

/// <summary>
/// <c>Application:WorldPlaces</c> (#967), read by identity: how long a dropped player's place is held, how long the
/// line of a full world may grow, how often a waiter checks in and how long a turn waits to be used.
/// </summary>
public sealed class WorldPlacesConfiguration
{
    public const string Section = "Application:WorldPlaces";

    /// <summary>The positions that check in often, so a free place is taken quickly.</summary>
    public const int FrontPositions = 10;

    [Range(1, 3600)] public int HeldPlaceSeconds { get; set; } = 90;
    [Range(1, 100_000)] public int QueueCapacity { get; set; } = 5000;
    [Range(1, 300)] public int FrontCheckInSeconds { get; set; } = 5;
    [Range(1, 900)] public int FrontTimeoutSeconds { get; set; } = 15;
    [Range(1, 300)] public int BackCheckInSeconds { get; set; } = 15;
    [Range(1, 900)] public int BackTimeoutSeconds { get; set; } = 45;
    [Range(5, 300)] public int TurnWindowSeconds { get; set; } = 45;

    public TimeSpan HeldPlace => TimeSpan.FromSeconds(HeldPlaceSeconds);
    public TimeSpan TurnWindow => TimeSpan.FromSeconds(TurnWindowSeconds);

    /// <summary>How soon a waiter at <paramref name="position"/> (from 1) is told to check in again.</summary>
    public TimeSpan CheckInAfter(int position) =>
        TimeSpan.FromSeconds(position <= FrontPositions ? FrontCheckInSeconds : BackCheckInSeconds);

    /// <summary>How long a waiter at <paramref name="position"/> stays in the line without checking in.</summary>
    public TimeSpan TimeoutAt(int position) =>
        TimeSpan.FromSeconds(position <= FrontPositions ? FrontTimeoutSeconds : BackTimeoutSeconds);

    /// <summary>A waiter that checks in when told is never removed for it: each timeout is above its interval.</summary>
    public bool IsValid() => FrontTimeoutSeconds > FrontCheckInSeconds && BackTimeoutSeconds > BackCheckInSeconds;
}
