namespace Avalon.Network.Packets.Abilities;

/// <summary>
/// What an ability is (#1178), authored on each row: the tags its damage and healing scale by. An ability is at most
/// one of <see cref="Attack" />, <see cref="Spell" /> and <see cref="Heal" />; a Hostile ability that deals damage is an
/// Attack or a Spell, and an Ally ability that heals is a Heal. <see cref="DamageOverTime" /> is never authored on an
/// ability: a damage-over-time aura adds it to its source's tags. Append-only.
/// </summary>
[Flags]
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
