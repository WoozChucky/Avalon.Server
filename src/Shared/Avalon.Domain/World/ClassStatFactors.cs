using Avalon.Common;

namespace Avalon.Domain.World;

/// <summary>
/// How one class turns its attributes into power and damage (#506). One row per class, reloaded with the combat area;
/// the stats calculator reads it. Health, crit and defence are the same for every class (#1178): the formula's life per
/// Stamina, base crit and conversions.
/// The fractional types are the ones the calculator's constants had, so the seed reproduces them bit for
/// bit. The whole-number factor is held as long, the bigint the column is, so a row outside the
/// uint range reaches the load's validation, which refuses it by name, instead of failing EF's read.
/// </summary>
public class ClassStatFactors
{
    public CharacterClass Class { get; set; }

    public double PowerPerIntellect { get; set; }

    public double PowerPerAgility { get; set; }

    /// <summary>
    /// A pool that is always this size whatever the row and gear say (Fury); null for a pool that grows.
    /// 0 to uint.MaxValue when set: a check constraint and the load both refuse others.
    /// </summary>
    public long? FixedPower { get; set; }

    public double AttackPerStrength { get; set; }

    public double AttackPerAgility { get; set; }

    public double AbilityPerIntellect { get; set; }
}
