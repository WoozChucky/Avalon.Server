using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>How a capstone reshapes its ability (#1181): the stored numbers, cast from the World's by ordinal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TalentModifierKind
{
    /// <summary>Value more projectiles, fired in a fan 10 degrees apart; a unit is affected once per cast.</summary>
    AddedProjectiles = 1,
    /// <summary>Where the projectile ends on a unit, a circle of Value / 10 metres hits the others at Value2 %.</summary>
    ImpactExplosion = 2,
    /// <summary>A circle's radius, and an explosion's, times (1 + Value / 100).</summary>
    AreaPct = 3,
    /// <summary>A cone's reach times (1 + Value / 100).</summary>
    ConeReachPct = 4,
    /// <summary>A cone's arc times (1 + Value / 100), at most 360 degrees.</summary>
    ConeArcPct = 5,
    /// <summary>The ability's aura may stack Value more times from this caster.</summary>
    AuraStacks = 6,
    /// <summary>The ability's aura lasts times (1 + Value / 100).</summary>
    AuraDurationPct = 7,
    /// <summary>Each unit affected is hit, or healed, again at once at Value % of the amount.</summary>
    SecondHit = 8,
}
