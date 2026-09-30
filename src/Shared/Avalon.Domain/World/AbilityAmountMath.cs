using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;

namespace Avalon.Domain.World;

/// <summary>
/// The per-hit arithmetic of an ability (#669), shared by the world server's combat and the api's tooltips
/// so the two never disagree: <c>effectValue + scaling × stat + baseDamageCoefficient × weapon roll</c>,
/// floored as a hit nothing procs on, against no armour.
/// </summary>
public static class AbilityAmountMath
{
    /// <summary>The scripts that apply an ability's amount directly; any other script may do anything.</summary>
    public static readonly IReadOnlySet<string> DirectScripts = new HashSet<string>(StringComparer.Ordinal)
    {
        "CircleAbilityScript", "ConeAbilityScript", "ProjectileAbilityScript",
    };

    public static AbilityAmountKind KindOf(string? scriptName, AbilityAffects affects) =>
        scriptName is null || !DirectScripts.Contains(scriptName) ? AbilityAmountKind.None
        : affects == AbilityAffects.Ally ? AbilityAmountKind.Healing
        : AbilityAmountKind.Damage;

    /// <summary>The base before flooring. <paramref name="weaponRoll"/> is ignored when the coefficient is not above 0.</summary>
    public static double Sum(double effectValue, double scaling, double statValue, double baseDamageCoefficient,
        long weaponRoll)
    {
        double total = NonNegative(effectValue) + NonNegative(scaling) * statValue;
        if (NonNegative(baseDamageCoefficient) > 0)
            total += (float)baseDamageCoefficient * weaponRoll; // float product, as combat always evaluated it
        return total;
    }

    public static (uint Min, uint Max) Range(AbilityAmountKind kind, float effectValue, ScalingStat stat, float scaling,
        float baseDamageCoefficient, uint attackDamage, uint abilityDamage, uint weaponMin, uint weaponMax)
    {
        if (kind == AbilityAmountKind.None) return (0, 0);

        double statValue = stat == ScalingStat.Ability ? abilityDamage : attackDamage;
        long low = Math.Min(weaponMin, weaponMax);
        float min = (float)Sum(effectValue, scaling, statValue, baseDamageCoefficient, weaponMax > 0 ? low : 0);
        float max = (float)Sum(effectValue, scaling, statValue, baseDamageCoefficient, weaponMax);

        return kind == AbilityAmountKind.Healing
            ? (NormalHeal(min), NormalHeal(max))
            : (NormalDamage(min), NormalDamage(max));
    }

    public static uint NormalDamage(double baseDamage) => Math.Max(1u, Floor(NonNegative(baseDamage)));

    public static uint NormalHeal(double baseHeal) => Floor(NonNegative(baseHeal));

    private static double NonNegative(double value) => double.IsFinite(value) && value > 0 ? value : 0d;

    private static uint Floor(double value) => value >= uint.MaxValue ? uint.MaxValue : (uint)Math.Floor(value);
}
