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
    public void Stop_with_a_lower_bound_when_the_generator_saturates_and_after_two_unknown_steps()
    {
        var saturated = new RampDecider(Limits.Defaults, 500);
        saturated.Decide(Sample(50, genCpu: 0.5));
        Assert.Equal(RampAction.Rehold, saturated.Decide(Sample(100, genCpu: 0.9)).Action);
        Decision d = saturated.Decide(Sample(100, genCpu: 0.95));
        Assert.Equal((RampAction.Stop, RampOutcome.GeneratorSaturated, (int?)50), (d.Action, d.Outcome, d.Capacity));

        var blind = new RampDecider(Limits.Defaults, 500);
        Assert.Equal(RampAction.Rehold, blind.Decide(Sample(50, tickP99: null)).Action);
        Assert.Equal((RampAction.Stop, RampOutcome.Unknown), (blind.Decide(Sample(50, tickP99: null)) is var u ? (u.Action, u.Outcome) : default));

        var drops = new RampDecider(Limits.Defaults, 500);
        Assert.True(drops.Decide(Sample(50, drops: 3, genCpu: 0.7)).DropsMayBeGenerator);
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
            [LimitName.Gen2] = 0,
            [LimitName.GcPause] = 0.01,
            [LimitName.SaveP95] = 50,
            [LimitName.GenCpu] = genCpu,
            [LimitName.GenLag] = 1,
        };
        return new StepSample(bots, values, genCpu);
    }
}
