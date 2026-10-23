namespace Avalon.Domain.World;

/// <summary>
/// How a modifier applies (#1178): Flat adds the value; Increased adds it to the stat's sum of increases, which scales
/// what the unit has by (1 + sum / 100); More multiplies by (1 + value / 100) on its own.
/// </summary>
public enum AuraModifierKind : byte { Flat = 1, Increased = 2, More = 3 }
