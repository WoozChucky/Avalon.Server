using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;

namespace Avalon.Combat;

/// <summary>
/// How long an ability takes (#1178, owner decision D1), the one rule the cast system follows. Pure. The global
/// cooldown is no speed's and is not here.
/// </summary>
public static class AbilityPace
{
    /// <summary>Every cast time, a creature's included, over cast speed.</summary>
    public static float CastTime(AbilityMetadata ability, in CasterPace pace) =>
        Speed.Scale(ability.CastTime, pace.CastSpeedPct);

    /// <summary>
    /// The cooldown a cast sets. Any Attack whose row's Cooldown is 0 waits its caster's attack time over attack speed
    /// (#1181), every basic Attack among them (its Cooldown is 0, validated); any other basic (a spell basic, D1) its row
    /// over cast speed; every other ability its row over cooldown recovery. A <paramref name="creatureBasic" /> (a
    /// creature's basic, whatever its row) waits the creature's BaseAttackTime over attack speed, its SwingInterval.
    /// </summary>
    public static float Cooldown(AbilityMetadata ability, in CasterPace pace, bool creatureBasic = false)
    {
        if (creatureBasic)
            return Speed.Scale(pace.AttackTimeSeconds, pace.AttackSpeedPct);

        if ((ability.Tags & AbilityTags.Attack) != AbilityTags.None && ability.Cooldown == 0f)
            return Speed.Scale(pace.AttackTimeSeconds, pace.AttackSpeedPct);

        return (ability.Flags & AbilityFlags.Basic) != AbilityFlags.None
            ? Speed.Scale(ability.Cooldown, pace.CastSpeedPct)
            : Speed.Scale(ability.Cooldown, pace.CooldownRecoveryPct);
    }
}
