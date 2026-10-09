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
/// <see cref="RampOutcome.NoLimitReached"/>; a lower bound for <see cref="RampOutcome.GeneratorSaturated"/>.
/// </param>
/// <param name="Breaches">The limits this step breached; on a stop for a breach, the confirmed ones ("failed first").</param>
/// <param name="Blip">This step passed the re-hold of a breached step.</param>
/// <param name="DropsMayBeGenerator">Drops breached while the bot PC was busy, so the bots may be reading slowly.</param>
public sealed record Decision(
    RampAction Action, RampOutcome Outcome, int? Capacity, IReadOnlyList<Breach> Breaches, bool Blip, bool DropsMayBeGenerator);

/// <summary>
/// The ramp's decision rule. A step that breaches a limit, or cannot be judged, is held once more at the same count; a
/// second breach stops the ramp with the last passing count as its capacity (a lower bound if the bot PC was the limit),
/// a second unknown verdict stops it as unknown, and a passing re-hold is a blip the ramp continues past.
/// </summary>
/// <remarks>Stateful: one decider per ramp, asked once per step, in order.</remarks>
public sealed class RampDecider(IReadOnlyList<Limit> limits, int maxBots)
{
    /// <summary>Above this bot PC CPU use, a drops breach may be the bots reading slowly rather than the server.</summary>
    private const double GeneratorBusyCpu = 0.60;

    private int? _lastPass;
    private bool _reholding;

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
                missing |= limit.Name != LimitName.Drops;
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
            bool blip = _reholding;
            _reholding = false;
            _lastPass = sample.Bots;
            return sample.Bots >= maxBots
                ? new Decision(RampAction.Stop, RampOutcome.NoLimitReached, sample.Bots, breaches, blip, dropsMayBeGenerator)
                : new Decision(RampAction.NextStep, RampOutcome.Running, null, breaches, blip, dropsMayBeGenerator);
        }

        if (!_reholding)
        {
            _reholding = true;
            return new Decision(RampAction.Rehold, RampOutcome.Running, null, breaches, false, dropsMayBeGenerator);
        }

        RampOutcome outcome = verdict == StepVerdict.Unknown ? RampOutcome.Unknown
            : breaches.Exists(b => Limits.IsGenerator(b.Name)) ? RampOutcome.GeneratorSaturated
            : RampOutcome.Capacity;
        return new Decision(RampAction.Stop, outcome, _lastPass ?? 0, breaches, false, dropsMayBeGenerator);
    }
}
