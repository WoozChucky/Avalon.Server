namespace Avalon.World.Characters;

/// <summary>
/// One character's movement budget (#1267): the input steps server time has earned it. Every input the character may
/// move on spends one step of <see cref="StepSeconds" />, and the budget refills by the server time that passed since
/// the last input, at one step per 1/60 s plus the clock drift allowance (<c>Game:Movement:ClockDriftAllowancePercent</c>,
/// 0.5 %), up to a cap of a few steps (<c>Game:Movement:InputCatchUp</c>). So inputs that queued behind a late tick
/// still move the character, a client whose clock runs a little fast is never held back by it, and a client sending
/// faster than that is held to it. An input over the budget is answered standing. Made once with the character, full
/// at its first input; World-side, never on ICharacter, and never saved. Tick thread only.
/// </summary>
public sealed class MovementBudget
{
    /// <summary>The steps one second of server time earns, before the drift allowance: the input step's rate.</summary>
    public const int StepsPerSecond = 60;

    /// <summary>The time one input step moves the character for.</summary>
    public const float StepSeconds = 1f / StepsPerSecond;

    // The steps held, a fraction of one included, and when the budget was last refilled (a TimeProvider timestamp);
    // 0 before the first input.
    private double _steps;
    private long _last;

    /// <summary>
    /// Whether this character's first input over the budget has been logged: the warning is written once per character,
    /// every refusal is counted. Never cleared, so it also tells that the character was ever refused. Set only by the
    /// input handler, so nothing outside Avalon.World can silence the warning.
    /// </summary>
    public bool Warned { get; internal set; }

    /// <summary>
    /// The most steps the budget holds for a catch-up allowance of <paramref name="catchUp" />: the whole steps it
    /// spans, at least 1 (250 ms is 15). Computed from ticks, so a catch-up of whole steps is not floored one short.
    /// </summary>
    public static int CapFor(TimeSpan catchUp)
    {
        double steps = catchUp.Ticks * (double)StepsPerSecond / TimeSpan.TicksPerSecond;
        return steps >= 1 ? (int)Math.Min(steps, int.MaxValue) : 1;
    }

    /// <summary>
    /// The steps one second of server time earns with a clock drift allowance of
    /// <paramref name="driftAllowancePercent" />: 60 x (1 + allowance / 100), 60.3 at the default 0.5 %. That is also
    /// the fastest a client can move: 0.5 % above its speed.
    /// </summary>
    public static double RefillRateFor(double driftAllowancePercent) =>
        StepsPerSecond * (1d + driftAllowancePercent / 100d);

    /// <summary>
    /// Refills the budget by the server time since the last call at <paramref name="stepsPerSecond" /> (see
    /// <see cref="RefillRateFor" />), holding at most <paramref name="cap" /> steps, and spends one step: true when there
    /// was one to spend. The first call finds the budget full.
    /// </summary>
    public bool TrySpend(long nowTimestamp, TimeProvider clock, int cap, double stepsPerSecond)
    {
        if (_last == 0)
        {
            _steps = cap;
        }
        else
        {
            double earned = clock.GetElapsedTime(_last, nowTimestamp).TotalSeconds * stepsPerSecond;
            _steps = Math.Min(cap, _steps + earned);
        }

        _last = nowTimestamp;
        if (_steps < 1d)
            return false;

        _steps -= 1d;
        return true;
    }
}
