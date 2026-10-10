namespace Avalon.LoadTest.Ramp;

/// <summary>
/// One step's measured values. A null (or absent) value means the series was missing, so the step cannot be judged on
/// it, unless the limit is one this world build does not export (<see cref="NotJudged"/>).
/// </summary>
/// <param name="Bots">The bot count the step held.</param>
/// <param name="GeneratorCpu">
/// The bot PC's CPU use over the step, a fraction of its cores: the same value as <c>Values[GenCpu]</c>, kept apart
/// because it feeds the slow-kicks flag (<see cref="Decision.SlowKicksMayBeGenerator"/>) whatever the limits are.
/// </param>
public sealed record StepSample(int Bots, IReadOnlyDictionary<LimitName, double?> Values, double GeneratorCpu)
{
    /// <summary>
    /// The limits whose series this world build does not export (<see cref="GcStallReadout.NotExported"/>,
    /// <see cref="SendReadout.NotExported"/>): the step is not judged on them, neither passed nor unknown, and the report
    /// says so.
    /// </summary>
    public IReadOnlySet<LimitName> NotJudged { get; init; } = new HashSet<LimitName>();
}
