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

    /// <summary>
    /// How long join tickets alone may keep a place no session claims (#967): from when the place became unclaimed (taken
    /// by a ticket, or left by a session's end, a held place included) until a session's activation or heartbeat claims it
    /// again. Past it, a further ticket is not kept on the place it has: it goes through the capacity step as a newcomer.
    /// </summary>
    [Range(1, 1440)] public int UnclaimedPlaceMinutes { get; set; } = 15;

    /// <summary>
    /// How long after it was given an unused turn is kept at most while the world is not available (#967): a check-in
    /// then restarts the turn's window, never past this from when the turn was given, so a turn survives a stop or a
    /// maintenance window of that length but readiness that comes and goes cannot keep it for ever.
    /// </summary>
    [Range(1, 1440)] public int PausedTurnMinutes { get; set; } = 30;

    public TimeSpan HeldPlace => TimeSpan.FromSeconds(HeldPlaceSeconds);
    public TimeSpan PausedTurn => TimeSpan.FromMinutes(PausedTurnMinutes);
    public TimeSpan TurnWindow => TimeSpan.FromSeconds(TurnWindowSeconds);
    public TimeSpan UnclaimedPlace => TimeSpan.FromMinutes(UnclaimedPlaceMinutes);

    /// <summary>How soon a waiter at <paramref name="position"/> (from 1) is told to check in again.</summary>
    public TimeSpan CheckInAfter(int position) =>
        TimeSpan.FromSeconds(position <= FrontPositions ? FrontCheckInSeconds : BackCheckInSeconds);

    /// <summary>How long a waiter at <paramref name="position"/> stays in the line without checking in.</summary>
    public TimeSpan TimeoutAt(int position) =>
        TimeSpan.FromSeconds(position <= FrontPositions ? FrontTimeoutSeconds : BackTimeoutSeconds);

    /// <summary>A waiter that checks in when told is never removed for it: each timeout is above its interval.</summary>
    public bool IsValid() => FrontTimeoutSeconds > FrontCheckInSeconds && BackTimeoutSeconds > BackCheckInSeconds;

    /// <summary>
    /// A place held after a drop keeps its whole grace and is never swept past the horizon (which locks its account out):
    /// the unclaimed horizon is at least the held place's grace plus <see cref="HeldPlaceMargin"/>.
    /// </summary>
    public bool CoversHeldPlace() => UnclaimedPlace >= HeldPlace + HeldPlaceMargin;

    /// <summary>A paused turn is kept at least as long as one turn window: <see cref="PausedTurn"/> covers <see cref="TurnWindow"/>.</summary>
    public bool CoversTurnWindow() => PausedTurn >= TurnWindow;

    /// <summary>The least the unclaimed horizon runs past the held place's grace.</summary>
    public static readonly TimeSpan HeldPlaceMargin = TimeSpan.FromMinutes(1);
}
