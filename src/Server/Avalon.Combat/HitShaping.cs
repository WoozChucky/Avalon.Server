using Avalon.Network.Packets.Abilities;

namespace Avalon.Combat;

/// <summary>
/// How one hit of an ability is shaped (#1181 Part D): the share of the ability's amount it deals, in percent, and the
/// tags it carries beyond the ability's own, which the offence modifiers read as theirs. A second hit is the ability at
/// its second-hit share; an explosion is its explosion share with <see cref="AbilityTags.Area" /> added.
/// </summary>
/// <remarks>
/// There is no default on any member that takes one: <c>default(HitShaping)</c> is a share of 0 %, so every caller names
/// <see cref="Whole" /> or its share.
/// </remarks>
public readonly record struct HitShaping(float AmountPct, AbilityTags ExtraTags)
{
    /// <summary>The ability's own hit: all of its amount, with its own tags.</summary>
    public static HitShaping Whole => new(100f, AbilityTags.None);
}
