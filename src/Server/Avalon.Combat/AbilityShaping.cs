using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Public.Abilities;

namespace Avalon.Combat;

/// <summary>
/// The capstone modifiers shaping one ability (#1181 Part D), summed per kind over the capstones a character holds, and
/// what they make of its metadata. Value equality makes "the same shape" a compare of a few numbers, so a talent change
/// tells whether an ability's shape moved without building anything.
/// </summary>
public record struct AbilityShaping
{
    /// <summary>The most projectiles one cast fires, the ability's own included: each lane is an object of its own.</summary>
    public const int MaxProjectiles = 9;

    private int _addedProjectiles;
    private int _explosionTenths;
    private int _explosionEffectiveness;
    private int _areaPct;
    private int _reachPct;
    private int _arcPct;
    private int _auraStacks;
    private int _auraDurationPct;
    private int _secondHit;

    /// <summary>True when no modifier has been added: the ability keeps its template's shape.</summary>
    public readonly bool IsNone =>
        (_addedProjectiles | _explosionTenths | _explosionEffectiveness | _areaPct | _reachPct | _arcPct | _auraStacks |
         _auraDurationPct | _secondHit) == 0;

    /// <summary>
    /// Adds one modifier row. <see cref="TalentModifierKind.ImpactExplosion" /> adds <paramref name="value" /> to the
    /// explosion's radius, in tenths of a metre, and <paramref name="value2" /> to its effectiveness.
    /// </summary>
    public void Add(TalentModifierKind kind, int value, int? value2)
    {
        switch (kind)
        {
            case TalentModifierKind.AddedProjectiles:
                _addedProjectiles += value;
                break;
            case TalentModifierKind.ImpactExplosion:
                _explosionTenths += value;
                _explosionEffectiveness += value2 ?? 0;
                break;
            case TalentModifierKind.AreaPct:
                _areaPct += value;
                break;
            case TalentModifierKind.ConeReachPct:
                _reachPct += value;
                break;
            case TalentModifierKind.ConeArcPct:
                _arcPct += value;
                break;
            case TalentModifierKind.AuraStacks:
                _auraStacks += value;
                break;
            case TalentModifierKind.AuraDurationPct:
                _auraDurationPct += value;
                break;
            case TalentModifierKind.SecondHit:
                _secondHit += value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a talent modifier kind");
        }
    }

    /// <summary>
    /// A copy of <paramref name="metadata" /> shaped by the summed modifiers: a cone's reach and arc (at most 360
    /// degrees), a circle's radius, a projectile's count (at most <see cref="MaxProjectiles" />), the explosion's radius
    /// (widened by the area modifier) and effectiveness, and the second hit, stack and duration shares as summed. A reach,
    /// arc, area or projectile modifier that does not fit the ability's shape leaves its shape fields unchanged; the other
    /// fields are set whatever the shape, and the talent catalog refuses a modifier that does not fit
    /// (<see cref="TalentModifierRules" />).
    /// </summary>
    public readonly AbilityMetadata Apply(AbilityMetadata metadata)
    {
        bool cone = metadata.Shape == AbilityShape.Cone;
        float area = Scale(_areaPct);
        return new AbilityMetadata(metadata)
        {
            Reach = cone ? metadata.Reach * Scale(_reachPct) : metadata.Reach,
            ArcDegrees = cone ? Math.Min(360f, metadata.ArcDegrees * Scale(_arcPct)) : metadata.ArcDegrees,
            Radius = metadata.Shape == AbilityShape.Circle ? metadata.Radius * area : metadata.Radius,
            ProjectileCount = metadata.Shape == AbilityShape.Projectile
                ? Math.Min(MaxProjectiles, 1 + _addedProjectiles)
                : 1,
            ExplosionRadius = _explosionTenths / 10f * area,
            ExplosionEffectiveness = _explosionEffectiveness,
            SecondHitEffectiveness = _secondHit,
            AuraStacksBonus = (uint)_auraStacks,
            AuraDurationPct = _auraDurationPct,
        };
    }

    private static float Scale(int pct) => 1f + pct / 100f;
}
