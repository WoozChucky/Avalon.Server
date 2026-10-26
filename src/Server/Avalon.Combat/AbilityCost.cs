using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.Combat;

public enum CostCheck
{
    Payable,
    NotEnoughPower,
}

/// <summary>
/// The one power rule (#521 item 2, #1181). A cost above 0 is spent from mana, the one pool every character casts
/// from: only a caster on Mana holding at least the cost may pay it. A cost of 0 needs no pool at all, and a caster with
/// no pool (a creature) is refused anything that costs. <c>CastAbilityHandler</c> checks it, and
/// <c>InstanceAbilityCastSystem</c> checks it again and pays it when it takes the cast, on the queued path and the
/// instant path alike; a queued cast is paid once, when it is queued.
/// </summary>
public static class AbilityCost
{
    public static CostCheck Check(IUnit caster, AbilityMetadata meta)
    {
        if (meta.Cost == 0)
        {
            return CostCheck.Payable;
        }

        if (caster.PowerType != PowerType.Mana || (caster.CurrentPower ?? 0) < meta.Cost)
        {
            return CostCheck.NotEnoughPower;
        }

        return CostCheck.Payable;
    }

    /// <summary>Call only after <see cref="Check" /> answered Payable: it spends the cost from the caster's mana.</summary>
    public static void Pay(IUnit caster, AbilityMetadata meta)
    {
        if (meta.Cost > 0)
        {
            caster.CurrentPower = (caster.CurrentPower ?? 0) - meta.Cost;
        }
    }
}
