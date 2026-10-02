namespace Avalon.World.Combat;

/// <summary>How an ability's hit went, for what follows it: only a hit that landed applies the ability's aura.</summary>
public enum HitOutcome
{
    /// <summary>The target passes over every hit: invulnerable, a corpse, or a creature walking home.</summary>
    Ignored,

    Dodged,

    Landed,
}
