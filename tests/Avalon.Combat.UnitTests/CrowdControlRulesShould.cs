using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.Combat.UnitTests;

/// <summary>The crowd-control rules the world server applies: duration scaling, creature resistance, PvP diminishing.</summary>
public class CrowdControlRulesShould
{
    private static AuraTemplate Aura(CrowdControl control) => new()
    {
        Id = new AuraId(12),
        Name = "Stunned",
        Kind = AuraKind.Harmful,
        DurationMs = 1500,
        Stacking = AuraStacking.Refresh,
        Control = control,
    };

    /// <summary>Crippled: no Control bits, a slow through its negative movement speed modifier alone.</summary>
    private static AuraTemplate Crippled()
    {
        AuraTemplate t = Aura(CrowdControl.None);
        t.Modifiers.Add(new AuraStatModifier { Stat = AuraStat.MovementSpeed, Kind = AuraModifierKind.Flat, Value = -30f });
        return t;
    }

    public static TheoryData<string, double, Func<double>> Cases() => new()
    {
        { "1500 ms at 50 %", 750, () => CrowdControlRules.Scale(1500, 50f) },
        { "1001 ms at 50 % rounds down", 500, () => CrowdControlRules.Scale(1001, 50f) },
        { "1500 ms at 0 % is refused", 0, () => CrowdControlRules.Scale(1500, 0f) },
        { "a stun and root takes the lower of stun 0 and root 50", 0,
            () => CrowdControlRules.ResistPct(Aura(CrowdControl.Stun | CrowdControl.Root), 0f, 50f, 100f, 100f) },
        { "a slow takes the slow percentage", 50, () => CrowdControlRules.ResistPct(Crippled(), 100f, 100f, 100f, 50f) },
        { "an aura that grants no control is never resisted", 100,
            () => CrowdControlRules.ResistPct(Aura(CrowdControl.None), 0f, 0f, 0f, 0f) },
        { "the first landing in the window is full", 100, () => CrowdControlRules.DiminishPct(0) },
        { "the second is half", 50, () => CrowdControlRules.DiminishPct(1) },
        { "the third is a quarter", 25, () => CrowdControlRules.DiminishPct(2) },
        { "the fourth is refused", 0, () => CrowdControlRules.DiminishPct(3) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Scale_resist_and_diminish(string @case, double expected, Func<double> actual)
    {
        double got = actual();

        Assert.True(expected == got, $"{@case}: expected {expected}, got {got}");
    }
}
