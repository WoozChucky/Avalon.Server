using Avalon.LoadTest.Ramp;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class RampDeciderShould
{
    public static TheoryData<string, (int bots, double tickP99)[], (RampAction action, RampOutcome outcome, int? capacity)[]> Cases => new()
    {
        { "pass, pass, then a confirmed breach: capacity is the last pass",
          [(50, 5), (100, 8), (150, 20), (150, 21)],
          [(RampAction.NextStep, RampOutcome.Running, null), (RampAction.NextStep, RampOutcome.Running, null),
           (RampAction.Rehold, RampOutcome.Running, null), (RampAction.Stop, RampOutcome.Capacity, 100)] },
        { "a breach that passes its re-hold is a blip and the ramp goes on",
          [(50, 5), (100, 20), (100, 9), (150, 9)],
          [(RampAction.NextStep, RampOutcome.Running, null), (RampAction.Rehold, RampOutcome.Running, null),
           (RampAction.NextStep, RampOutcome.Running, null), (RampAction.Stop, RampOutcome.NoLimitReached, 150)] },
        { "max reached with no breach",
          [(50, 5), (100, 5), (150, 5)],
          [(RampAction.NextStep, RampOutcome.Running, null), (RampAction.NextStep, RampOutcome.Running, null),
           (RampAction.Stop, RampOutcome.NoLimitReached, 150)] },
        { "a breach on the first step: capacity 0",
          [(50, 30), (50, 30)],
          [(RampAction.Rehold, RampOutcome.Running, null), (RampAction.Stop, RampOutcome.Capacity, 0)] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Apply_the_rehold_rule(string _, (int bots, double tickP99)[] steps, (RampAction action, RampOutcome outcome, int? capacity)[] expected)
    {
        var decider = new RampDecider(Limits.Defaults, maxBots: 150);
        for (int i = 0; i < steps.Length; i++)
        {
            Decision d = decider.Decide(Sample(steps[i].bots, tickP99: steps[i].tickP99));
            Assert.Equal(expected[i], (d.Action, d.Outcome, d.Capacity));
        }
    }

    [Fact]
    public void Judge_saturation_unknown_steps_and_drops_and_refuse_invalid_limits()
    {
        var saturated = new RampDecider(Limits.Defaults, 500);
        saturated.Decide(Sample(50, genCpu: 0.5));
        Assert.Equal(RampAction.Rehold, saturated.Decide(Sample(100, genCpu: 0.9)).Action);
        Decision d = saturated.Decide(Sample(100, genCpu: 0.95));
        Assert.Equal((RampAction.Stop, RampOutcome.GeneratorSaturated, (int?)50), (d.Action, d.Outcome, d.Capacity));

        var blind = new RampDecider(Limits.Defaults, 500);
        Assert.Equal(RampAction.Rehold, blind.Decide(Sample(50, tickP99: null)).Action);
        Assert.Equal((RampAction.Stop, RampOutcome.Unknown, (int?)null), (blind.Decide(Sample(50, tickP99: null)) is var u ? (u.Action, u.Outcome, u.Capacity) : default));

        // An unknown step between them does not confirm a breach, nor does a breach confirm an unknown step.
        var unknownThenBreach = new RampDecider(Limits.Defaults, 500);
        unknownThenBreach.Decide(Sample(50));
        Assert.Equal(RampAction.Rehold, unknownThenBreach.Decide(Sample(100, tickP99: null)).Action);
        Assert.Equal(RampAction.Rehold, unknownThenBreach.Decide(Sample(100, tickP99: 30)).Action);
        Decision c = unknownThenBreach.Decide(Sample(100, tickP99: 30));
        Assert.Equal((RampAction.Stop, RampOutcome.Capacity, (int?)50), (c.Action, c.Outcome, c.Capacity));

        var breachThenUnknown = new RampDecider(Limits.Defaults, 500);
        breachThenUnknown.Decide(Sample(50));
        Assert.Equal(RampAction.Rehold, breachThenUnknown.Decide(Sample(100, tickP99: 30)).Action);
        Assert.Equal(RampAction.Rehold, breachThenUnknown.Decide(Sample(100, tickP99: double.NaN)).Action);
        Decision p = breachThenUnknown.Decide(Sample(100));
        Assert.Equal((RampAction.NextStep, false), (p.Action, p.Blip));
        breachThenUnknown.Decide(Sample(150, tickP99: 30));
        Assert.Equal(RampAction.Rehold, breachThenUnknown.Decide(Sample(150, tickP99: null)).Action);
        Assert.Equal(RampOutcome.Unknown, breachThenUnknown.Decide(Sample(150, tickP99: null)).Outcome);

        // Verdicts that keep alternating: three re-holds at most, then the step is inconclusive.
        var alternating = new RampDecider(Limits.Defaults, 500);
        alternating.Decide(Sample(50));
        (RampAction, RampOutcome, int?) rehold = (RampAction.Rehold, RampOutcome.Running, null);
        Assert.Equal([rehold, rehold, rehold, (RampAction.Stop, RampOutcome.Unknown, 50)],
            new double?[] { 30, null, 30, null }.Select(t => alternating.Decide(Sample(100, tickP99: t))).Select(d => (d.Action, d.Outcome, d.Capacity)));

        var drops = new RampDecider(Limits.Defaults, 500);
        Assert.True(drops.Decide(Sample(50, drops: 3, genCpu: 0.7)).DropsMayBeGenerator);

        // A drops value that is not finite cannot be judged on, unlike a missing one (no drops): the step is unknown.
        var nanDrops = new RampDecider(Limits.Defaults, 500);
        Assert.Equal(RampAction.Rehold, nanDrops.Decide(Sample(50, drops: double.PositiveInfinity)).Action);
        Assert.Equal(RampOutcome.Unknown, nanDrops.Decide(Sample(50, drops: double.NaN)).Outcome);

        // A limit the world build does not export is not judged: its missing value makes no unknown step.
        StepSample passing = Sample(50);
        StepSample noGcStall = passing with
        {
            Values = new Dictionary<LimitName, double?>(passing.Values) { [LimitName.GcStall] = null },
            NotJudged = new HashSet<LimitName> { LimitName.GcStall },
        };
        Assert.Equal(RampAction.NextStep, new RampDecider(Limits.Defaults, 500).Decide(noGcStall).Action);

        // The limits the decider judges on refuse an unknown name, a negative value and a fraction above 1.
        foreach (string refused in new[] { "tick-p98=20", "tick-p99=-1", "memory=1.5" })
            Assert.Throws<CommandLineException>(() => Limits.WithOverrides([refused]));
    }

    private static StepSample Sample(int bots, double? tickP99 = 5, double genCpu = 0.2, double drops = 0)
    {
        // Every other limit at a passing value.
        var values = new Dictionary<LimitName, double?>
        {
            [LimitName.TickP99] = tickP99,
            [LimitName.Tps] = 60,
            [LimitName.AckP95] = 20,
            [LimitName.Drops] = drops,
            [LimitName.Admission] = 0,
            [LimitName.Memory] = 0.3,
            [LimitName.GcStall] = 5,
            [LimitName.GcPause] = 0.01,
            [LimitName.SaveP95] = 50,
            [LimitName.GenCpu] = genCpu,
            [LimitName.GenLag] = 1,
        };
        return new StepSample(bots, values, genCpu);
    }
}
