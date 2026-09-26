using Avalon.Network.Packets.State;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities;

public enum CostCheck
{
    Payable,
    NotEnoughPower,

    /// <summary>A cost above 0 on a caster with no pool to spend (PowerType.None). Nothing the player can fix.</summary>
    WrongPowerType,
}

/// <summary>
/// The one power rule (#521 item 2). A cost above 0 needs a pool the cast spends (Mana, Energy or
/// Fury) holding at least the cost. <c>CastAbilityHandler</c> checks it, and
/// <c>InstanceAbilityCastSystem</c> checks it again and pays it, on the queued path and the instant
/// path alike. Fury is spent like the others, but nothing generates it yet: a Warrior enters full and
/// does not regenerate until #526.
/// </summary>
public static class AbilityCost
{
    public static CostCheck Check(IUnit caster, AbilityMetadata meta)
    {
        if (meta.Cost == 0)
        {
            return CostCheck.Payable;
        }

        if (caster.PowerType is not (PowerType.Mana or PowerType.Energy or PowerType.Fury))
        {
            return CostCheck.WrongPowerType;
        }

        return (caster.CurrentPower ?? 0) < meta.Cost ? CostCheck.NotEnoughPower : CostCheck.Payable;
    }

    /// <summary>Call only after <see cref="Check" /> answered Payable.</summary>
    public static void Pay(IUnit caster, AbilityMetadata meta)
    {
        if (meta.Cost > 0)
        {
            caster.CurrentPower = (caster.CurrentPower ?? 0) - meta.Cost;
        }
    }
}
