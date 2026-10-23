using Avalon.Network.Packets.Combat;

namespace Avalon.Combat;

/// <summary>
/// Global added damage to attacks or to spells (#1178), rolled inclusive and scaled by the ability's damage
/// effectiveness. A parameter of the pure maths, never stored; default (a Max of 0) adds none.
/// </summary>
public readonly record struct AddedDamage(DamageType Type, uint Min, uint Max);
