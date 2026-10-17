using Avalon.Hosting.Networking;

namespace Avalon.World.Abilities;

/// <summary>
/// Lag compensation's limits and conversions (#1082). A player's instant cast is judged against the units where that
/// player saw them: the world tick the cast names (<c>CCastAbilityPacket.ViewTick</c>), clamped to
/// <see cref="MaxRewindTicks" /> behind the tick it arrives on. The cap is the only trust boundary: a client may always
/// claim the whole of it.
/// </summary>
public static class LagCompensation
{
    /// <summary>The furthest a cast is rewound: 250 ms at 60 Hz.</summary>
    public const int MaxRewindTicks = 15;

    /// <summary>The longest a telegraph's hit on a player waits for that player's late input: about 100 ms.</summary>
    public const int MaxDodgeWaitTicks = 6;

    /// <summary>
    /// The furthest, in metres, a rewound hit query allows any unit to have moved in one tick (60 m/s): the box of grid
    /// cells it visits, which hold present positions, is widened by this for every tick rewound.
    /// </summary>
    public const float MaxStepPerTick = 1f;

    // A world tick in TimeSpan ticks, in double so a round trip converts without integer truncation.
    private const double TicksPerWorldTick = (double)TimeSpan.TicksPerSecond / WorldTicks.PerSecond;

    /// <summary>
    /// How many ticks a cast naming <paramref name="viewTick" />, taken on <paramref name="nowTick" />, is rewound: none when
    /// the cast named no tick (0, an older client) or one at or ahead of now, else the ticks between, at most
    /// <see cref="MaxRewindTicks" />.
    /// </summary>
    public static int RewindTicks(long viewTick, long nowTick)
    {
        if (viewTick <= 0)
            return 0;

        return (int)Math.Clamp(nowTick - viewTick, 0L, MaxRewindTicks);
    }

    /// <summary>
    /// Half of <paramref name="roundTripUtcTicks" /> (a connection's <c>RoundTripTime</c>, in TimeSpan ticks), in whole
    /// world ticks, rounded to the nearest; 0 for a round trip that is not above 0.
    /// </summary>
    public static int OneWayTicks(long roundTripUtcTicks)
    {
        if (roundTripUtcTicks <= 0)
            return 0;

        return (int)Math.Round(roundTripUtcTicks / 2.0 / TicksPerWorldTick, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// How far a cast's hits are rewound (#1082), fixed when it is taken: <see cref="HitRewindTicks" /> for the hits its
/// script makes as it fires (an instant circle or cone), <see cref="ProjectileRewindTicks" /> for a projectile's sweep
/// over its flight, shorter by the caster's one-way delay because the client draws projectiles where the server has them.
/// The default rewinds nothing: a queued cast's, a creature's and an item's.
/// </summary>
public readonly record struct CastView(int HitRewindTicks, int ProjectileRewindTicks);
