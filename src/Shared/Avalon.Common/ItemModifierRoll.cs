namespace Avalon.Common;

/// <summary>
/// One modifier an item instance rolled (#1179), frozen at drop: the catalog modifier it came from (0 for a unique's own
/// line), its tier (0 for none), the stat it gives and its value. A single-valued line has <see cref="Min" /> equal to
/// <see cref="Max" />; a range stat (<see cref="StatTypes.IsRange" />) carries its low and high end.
/// </summary>
public readonly record struct ItemModifierRoll(ushort ModifierId, byte Tier, StatType Stat, int Min, int Max);
