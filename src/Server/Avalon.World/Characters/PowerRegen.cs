using Avalon.World.Configuration;
using Avalon.World.Public.Enums;

namespace Avalon.World.Characters;

/// <summary>
/// The one Mana and Energy regeneration rule. Called by <c>CharacterEntity.Update</c> every tick and by the balance
/// simulator, so the two cannot drift. Fury never regenerates and is not asked here.
/// </summary>
public static class PowerRegen
{
    /// <summary>
    /// A 1/60 s TimeSpan is truncated to whole ticks, so sixty of them fall a few millionths of a second short of one;
    /// the tolerance keeps that from costing a whole point, as Fury decay's does. What is left may dip that far below
    /// 0 and is repaid by the next step.
    /// </summary>
    private const double WholePointTolerance = 1e-3;

    /// <summary>The stat a class regenerates from: Intellect for Mana users, Agility for Energy, none otherwise.</summary>
    public static uint StatOf(CharacterClass characterClass, DerivedCharacterStats stats) => characterClass switch
    {
        CharacterClass.Wizard or CharacterClass.Healer => stats.Intellect,
        CharacterClass.Hunter => stats.Agility,
        _ => 0,
    };

    /// <summary>
    /// Power regained over one step of <paramref name="deltaSeconds" />. <c>regenStat x coefficient x dt</c>, the
    /// coefficient chosen by whether the character is in combat, is added to <paramref name="carry" />; the whole
    /// points in it are returned and the fraction stays for the next step, so the per-second rate holds at any tick
    /// rate. While a recent cast suppresses regeneration, or without a stat, it returns 0 and clears the carry, as
    /// Fury decay clears its remainder whenever it does not run: a fraction earned before a cast is not paid after it.
    /// </summary>
    public static uint Amount(RegenConfiguration config, uint regenStat, bool inCombat, bool castSuppressed,
        double deltaSeconds, ref double carry)
    {
        if (castSuppressed || regenStat == 0)
        {
            carry = 0d;
            return 0;
        }

        double coefficient = inCombat ? config.PowerRegenInCombatPerStat : config.PowerRegenOutOfCombatPerStat;
        return TakeWholePoints(regenStat * coefficient * deltaSeconds, ref carry);
    }

    /// <summary>
    /// Adds <paramref name="earned" /> to <paramref name="carry" />, takes the whole points out of it and returns
    /// them, leaving the fraction for the next step. Shared with the entity's out-of-combat health regeneration,
    /// which carries its own fraction the same way.
    /// </summary>
    internal static uint TakeWholePoints(double earned, ref double carry)
    {
        carry += earned;

        double owed = Math.Floor(carry + WholePointTolerance);
        if (owed <= 0d)
            return 0;

        uint whole = owed >= uint.MaxValue ? uint.MaxValue : (uint)owed;
        carry -= whole;
        return whole;
    }
}
