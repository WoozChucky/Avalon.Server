using System.Text.Json.Serialization;

namespace Avalon.Api.Contract;

/// <summary>
/// What an ability is (#1178): the tags its damage and healing scale by, as names joined by commas
/// (<c>"Attack, Melee, Area"</c>; <c>"None"</c> for none). An ability is at most one of Attack, Spell and Heal; a Hostile
/// ability that deals damage is an Attack or a Spell, and an Ally ability that heals is a Heal. DamageOverTime is never
/// an ability's: a damage-over-time aura adds it to its source's tags. The stored numbers, so values are only appended.
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AbilityTags
{
    None = 0,

    /// <summary>Deals its weapon's damage, scaled by the ability's damage effectiveness.</summary>
    Attack = 1,

    /// <summary>Deals its own spell base, grown by the caster's level.</summary>
    Spell = 2,

    /// <summary>Heals its own spell base, grown by the caster's level.</summary>
    Heal = 4,

    /// <summary>Hits an area: a circle or a cone.</summary>
    Area = 8,

    /// <summary>A projectile, and only a projectile.</summary>
    Projectile = 16,

    /// <summary>An attack at arm's length.</summary>
    Melee = 32,

    /// <summary>Damage dealt over time by an aura; an aura's tag, never an ability's.</summary>
    DamageOverTime = 64,
}
