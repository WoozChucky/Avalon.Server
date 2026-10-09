namespace Avalon.LoadTest.Ramp;

/// <summary>How a step was judged.</summary>
public enum StepVerdict { Pass, Breach, Unknown }

/// <summary>What the ramp does after a step.</summary>
public enum RampAction { NextStep, Rehold, Stop }

/// <summary>How the ramp ended, or <see cref="Running"/> while it goes on.</summary>
public enum RampOutcome { Running, Capacity, NoLimitReached, GeneratorSaturated, Unknown, Stopped }

/// <summary>A limit a step breached, with the step's value.</summary>
public sealed record Breach(LimitName Name, double Value, double Threshold);

/// <summary>
/// The decision after a step.
/// </summary>
/// <param name="Capacity">
/// Set when the ramp stops: the last passing bot count (0 when no step passed); the count reached for
/// <see cref="RampOutcome.NoLimitReached"/>; a lower bound for <see cref="RampOutcome.GeneratorSaturated"/>, and for
/// <see cref="RampOutcome.Unknown"/> (null when no step passed).
/// </param>
/// <param name="Breaches">The limits this step breached; on a stop for a breach, the confirmed ones ("failed first").</param>
/// <param name="Blip">This step passed the re-hold of a breached step (not of an unknown one).</param>
/// <param name="DropsMayBeGenerator">Drops breached while the bot PC was busy, so the bots may be reading slowly.</param>
public sealed record Decision(
    RampAction Action, RampOutcome Outcome, int? Capacity, IReadOnlyList<Breach> Breaches, bool Blip, bool DropsMayBeGenerator);

/// <summary>
/// The ramp's decision rule. A step that breaches a limit, or cannot be judged, is held once more at the same count. A
/// breach right after a breach stops the ramp with the last passing count as its capacity (a lower bound if the bot PC
/// was the limit), an unknown verdict right after an unknown one stops it as unknown, and a different verdict re-holds
/// again, at most <see cref="MaxReholds"/> times in a row before the ramp stops as unknown. A passing re-hold of a breach
/// is a blip the ramp continues past.
/// </summary>
/// <remarks>Stateful: one decider per ramp, asked once per step, in order.</remarks>
public sealed class RampDecider(IReadOnlyList<Limit> limits, int maxBots)
{
    /// <summary>Above this bot PC CPU use, a drops breach may be the bots reading slowly rather than the server.</summary>
    private const double GeneratorBusyCpu = 0.60;

    /// <summary>The most re-holds in a row at one bot count; a non-passing verdict past them stops the ramp as unknown.</summary>
    private const int MaxReholds = 3;

    private int? _lastPass;

    /// <summary>
    /// The verdict the step being re-held got (<see cref="StepVerdict.Breach"/> or <see cref="StepVerdict.Unknown"/>), or
    /// null when no re-hold is pending. Only the same verdict twice in a row stops the ramp: a breach needs a second breach
    /// to be confirmed, an unknown step a second unknown one.
    /// </summary>
    private StepVerdict? _pending;

    /// <summary>Re-holds in a row at the current count; a pass resets it.</summary>
    private int _reholds;

    /// <summary>Judges <paramref name="sample"/> and decides the ramp's next move.</summary>
    public Decision Decide(StepSample sample)
    {
        List<Breach> breaches = [];
        bool missing = false;
        foreach (Limit limit in limits)
        {
            double? value = sample.Values.TryGetValue(limit.Name, out double? v) ? v : null;
            if (value is null)
            {
                // A missing drops series is no drops (its query maps empty to 0); any other missing series is unknown.
                missing |= limit.Name != LimitName.Drops;
                continue;
            }

            // NaN or infinity fails both comparisons and would read as a pass: the step cannot be judged on it.
            if (!double.IsFinite(value.Value))
            {
                missing = true;
                continue;
            }

            if (limit.IsBreachedBy(value.Value))
                breaches.Add(new Breach(limit.Name, value.Value, limit.Threshold));
        }

        StepVerdict verdict = breaches.Count > 0 ? StepVerdict.Breach
            : missing ? StepVerdict.Unknown
            : StepVerdict.Pass;
        bool dropsMayBeGenerator = sample.GeneratorCpu > GeneratorBusyCpu && breaches.Exists(b => b.Name == LimitName.Drops);

        if (verdict == StepVerdict.Pass)
        {
            bool blip = _pending == StepVerdict.Breach;
            _pending = null;
            _reholds = 0;
            _lastPass = sample.Bots;
            return sample.Bots >= maxBots
                ? new Decision(RampAction.Stop, RampOutcome.NoLimitReached, sample.Bots, breaches, blip, dropsMayBeGenerator)
                : new Decision(RampAction.NextStep, RampOutcome.Running, null, breaches, blip, dropsMayBeGenerator);
        }

        if (_pending != verdict)
        {
            // Verdicts that keep alternating never confirm each other: past the last re-hold the step is inconclusive.
            if (_reholds == MaxReholds)
                return new Decision(RampAction.Stop, RampOutcome.Unknown, _lastPass, breaches, false, dropsMayBeGenerator);

            _reholds++;
            _pending = verdict;
            return new Decision(RampAction.Rehold, RampOutcome.Running, null, breaches, false, dropsMayBeGenerator);
        }

        if (verdict == StepVerdict.Unknown)
            return new Decision(RampAction.Stop, RampOutcome.Unknown, _lastPass, breaches, false, dropsMayBeGenerator);

        RampOutcome outcome = breaches.Exists(b => Limits.IsGenerator(b.Name)) ? RampOutcome.GeneratorSaturated : RampOutcome.Capacity;
        return new Decision(RampAction.Stop, outcome, _lastPass ?? 0, breaches, false, dropsMayBeGenerator);
    }
}
