using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.Combat;

public enum CostCheck
{
    Payable,
    NotEnoughPower,

    /// <summary>
    /// The cost is spent from a pool the caster does not have (#652): a Mana ability on a Fury caster, or any cost on a
    /// caster with no pool. The player is told; the ability cannot be cast by this caster.
    /// </summary>
    WrongPowerType,

    /// <summary>A cost above 0 whose row names no pool it is spent from (#652). Nothing the player can fix.</summary>
    NoCostPowerType,
}

/// <summary>
/// The one power rule (#521 item 2, #652). A cost above 0 is spent from the pool the ability names
/// (<see cref="AbilityMetadata.CostPowerType" />), and only a caster whose own pool is that one, holding at least the
/// cost, may pay it; points in another pool never count. A cost of 0 needs no pool at all.
/// <c>CastAbilityHandler</c> checks it, and <c>InstanceAbilityCastSystem</c> checks it again and pays it when it takes
/// the cast, on the queued path and the instant path alike; a queued cast is paid once, when it is queued.
/// </summary>
public static class AbilityCost
{
    public static CostCheck Check(IUnit caster, AbilityMetadata meta)
    {
        if (meta.Cost == 0)
        {
            return CostCheck.Payable;
        }

        if (meta.CostPowerType is not (PowerType.Mana or PowerType.Energy or PowerType.Fury))
        {
            return CostCheck.NoCostPowerType;
        }

        if (caster.PowerType != meta.CostPowerType)
        {
            return CostCheck.WrongPowerType;
        }

        return (caster.CurrentPower ?? 0) < meta.Cost ? CostCheck.NotEnoughPower : CostCheck.Payable;
    }

    /// <summary>Call only after <see cref="Check" /> answered Payable: it spends from the caster's pool, which is the ability's.</summary>
    public static void Pay(IUnit caster, AbilityMetadata meta)
    {
        if (meta.Cost > 0)
        {
            caster.CurrentPower = (caster.CurrentPower ?? 0) - meta.Cost;
        }
    }
}
