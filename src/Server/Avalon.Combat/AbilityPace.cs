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
    /// The cooldown a cast sets. A basic Attack waits its caster's attack time over attack speed (its row's Cooldown is 0,
    /// validated); any other basic (a spell basic, D1) its row over cast speed; every other ability its row over cooldown
    /// recovery. A <paramref name="creatureBasic" /> (a creature's basic, whatever its row) waits the creature's
    /// BaseAttackTime over attack speed, its SwingInterval.
    /// </summary>
    public static float Cooldown(AbilityMetadata ability, in CasterPace pace, bool creatureBasic = false)
    {
        if (creatureBasic)
            return Speed.Scale(pace.AttackTimeSeconds, pace.AttackSpeedPct);

        if ((ability.Flags & AbilityFlags.Basic) == AbilityFlags.None)
            return Speed.Scale(ability.Cooldown, pace.CooldownRecoveryPct);

        return (ability.Tags & AbilityTags.Attack) != AbilityTags.None
            ? Speed.Scale(pace.AttackTimeSeconds, pace.AttackSpeedPct)
            : Speed.Scale(ability.Cooldown, pace.CastSpeedPct);
    }
}
