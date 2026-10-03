using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.World.Auras;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

/// <summary>
/// One tick of an aura, or a hit an aura's script deals (auras). <see cref="Caster" /> is the caster only while it is
/// alive in the target's instance, else null: then nobody is credited. <see cref="BaseAmount" /> is the snapshot per
/// tick times the stacks. World-side.
/// </summary>
public readonly record struct PeriodicHit(IUnit? Caster, IUnit Target, AuraId Aura, float BaseAmount, AuraSnapshot Snapshot,
    AuraSource Source);
