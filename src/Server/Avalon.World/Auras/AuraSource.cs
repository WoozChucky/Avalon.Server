using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Auras;

/// <summary>
/// What an aura carries from the ability that applied it: which ability, whom it affects (so the aura system can check
/// that the aura still fits it when it is applied, since a reload can change either side), the threat its ticks give,
/// the heal threat per point, the power its caster gains per unit its ticks damage, the damage type the ability
/// names (#1178), which its damage ticks take, and the ability's metadata (#1178), whose base and tags a ticking aura's
/// total is rolled from when it is applied. None for an aura no ability applied (an item's, a script's): no ability
/// and nothing to fit, threat 1, no heal threat, no gain, no type, nothing to roll from.
/// </summary>
public readonly record struct AuraSource(
    AbilityId? AbilityId,
    float ThreatMultiplier,
    float HealThreatPerHp,
    uint PowerGainPerHit,
    AbilityAffects? Affects = null,
    DamageType? DamageType = null,
    AbilityMetadata? Ability = null)
{
    public static AuraSource None => new(null, 1f, 0f, 0u);

    public static AuraSource Of(IAbility ability) => Of(ability.AbilityId, ability.Metadata.Affects,
        ability.Metadata.ThreatMultiplier, ability.Metadata.HealThreatPerHp, ability.Metadata.PowerGainPerHit,
        ability.Metadata.DamageType, ability.Metadata);

    /// <summary>A restored aura's source (#1178): no metadata, since its total was rolled when it was first applied.</summary>
    public static AuraSource Of(AbilityTemplate row) =>
        Of(row.Id, row.Affects, row.ThreatMultiplier, row.HealThreatPerHp, row.PowerGainPerHit, row.DamageType, null);

    private static AuraSource Of(AbilityId id, AbilityAffects affects, float threat, float healThreat, int gain,
        DamageType? type, AbilityMetadata? ability) =>
        new(id, float.IsFinite(threat) && threat >= 0f ? threat : 1f,
            float.IsFinite(healThreat) && healThreat >= 0f ? healThreat : 0f, (uint)Math.Max(0, gain), affects, type,
            ability);
}
