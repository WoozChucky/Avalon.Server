using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Abilities;

namespace Avalon.World.Auras;

/// <summary>
/// What an aura carries from the ability that applied it: which ability, the threat its ticks give, the heal threat
/// per point and the power its caster gains per unit its ticks damage. None for an aura no ability applied (an item's,
/// a script's): threat 1, no heal threat, no gain.
/// </summary>
public readonly record struct AuraSource(AbilityId? AbilityId, float ThreatMultiplier, float HealThreatPerHp, uint PowerGainPerHit)
{
    public static AuraSource None => new(null, 1f, 0f, 0u);

    public static AuraSource Of(IAbility ability) => Of(ability.AbilityId, ability.Metadata.ThreatMultiplier,
        ability.Metadata.HealThreatPerHp, ability.Metadata.PowerGainPerHit);

    public static AuraSource Of(AbilityTemplate row) =>
        Of(row.Id, row.ThreatMultiplier, row.HealThreatPerHp, row.PowerGainPerHit);

    private static AuraSource Of(AbilityId id, float threat, float healThreat, int gain) =>
        new(id, float.IsFinite(threat) && threat >= 0f ? threat : 1f,
            float.IsFinite(healThreat) && healThreat >= 0f ? healThreat : 0f, (uint)Math.Max(0, gain));
}
