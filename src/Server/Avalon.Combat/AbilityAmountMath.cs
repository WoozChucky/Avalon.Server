using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Enums;

namespace Avalon.Combat;

/// <summary>
/// What a player is told an ability does (#669), shared by the world server and the api's tooltips so the two never
/// disagree: which abilities have a direct amount, and how a base is floored as a hit nothing procs on, against no
/// armour. The base itself is <see cref="OffenceMath" />'s (#1178).
/// </summary>
public static class AbilityAmountMath
{
    /// <summary>The scripts that apply an ability's amount directly; any other script may do anything.</summary>
    public static readonly IReadOnlySet<string> DirectScripts = new HashSet<string>(StringComparer.Ordinal)
    {
        "CircleAbilityScript", "ConeAbilityScript", "ProjectileAbilityScript",
    };

    /// <summary>
    /// What a player is told an ability does to each unit it hits: Damage or Healing; None for a script that may do
    /// anything, or for an ability whose Effects gives it no direct amount (it only applies an aura).
    /// </summary>
    public static AbilityAmountKind KindOf(string? scriptName, AbilityAffects affects, SpellEffect effects) =>
        scriptName is null || !DirectScripts.Contains(scriptName) || !AbilityRules.HasDirectEffect(effects, affects)
            ? AbilityAmountKind.None
            : affects == AbilityAffects.Ally ? AbilityAmountKind.Healing
            : AbilityAmountKind.Damage;

    public static uint NormalDamage(double baseDamage) => Math.Max(1u, Floor(NonNegative(baseDamage)));

    public static uint NormalHeal(double baseHeal) => Floor(NonNegative(baseHeal));

    private static double NonNegative(double value) => double.IsFinite(value) && value > 0 ? value : 0d;

    private static uint Floor(double value) => value >= uint.MaxValue ? uint.MaxValue : (uint)Math.Floor(value);
}
