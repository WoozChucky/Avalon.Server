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
/// from: only a caster on Mana holding at least the cost may pay it. A cost of 0 needs no pool at all, and any other
/// caster is refused anything that costs; none reaches the check today, since creatures cast free
/// (<c>InstanceAbilityCastSystem</c> skips the check for them, and no creature goes through <c>CastAbilityHandler</c>).
/// <c>CastAbilityHandler</c> checks it, and <c>InstanceAbilityCastSystem</c> checks it again and pays it when it takes
/// the cast, on the queued path and the instant path alike; a queued cast is paid once, when it is queued.
/// A character holding Blood Price (#1181 Part D, <c>KeystoneRule.LifeCost</c>) pays from current life instead
/// (<c>fromLife</c>): a cost is payable only when it leaves at least 1 life, and the refusal is the same NotEnoughPower.
/// Paying life is not damage: it meets no ward, armour or evasion, reports no hit and starts no combat, and mana is
/// untouched.
/// </summary>
public static class AbilityCost
{
    public static CostCheck Check(IUnit caster, AbilityMetadata meta, bool fromLife = false)
    {
        if (meta.Cost == 0)
        {
            return CostCheck.Payable;
        }

        if (fromLife)
        {
            return caster.CurrentHealth > meta.Cost ? CostCheck.Payable : CostCheck.NotEnoughPower;
        }

        if (caster.PowerType != PowerType.Mana || (caster.CurrentPower ?? 0) < meta.Cost)
        {
            return CostCheck.NotEnoughPower;
        }

        return CostCheck.Payable;
    }

    /// <summary>
    /// Call only after <see cref="Check" /> answered Payable with the same <paramref name="fromLife" />: it spends the cost
    /// from the caster's mana, or from its current life.
    /// </summary>
    public static void Pay(IUnit caster, AbilityMetadata meta, bool fromLife = false)
    {
        if (meta.Cost == 0)
        {
            return;
        }

        if (fromLife)
        {
            caster.CurrentHealth -= meta.Cost;
            return;
        }

        caster.CurrentPower = (caster.CurrentPower ?? 0) - meta.Cost;
    }
}
