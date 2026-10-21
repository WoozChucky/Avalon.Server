using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>
/// What a unit's crowd control lets it do, read from the state its auras cache (<see cref="UnitAuras.Control" />): no
/// scan, no allocation, so player input, casts and creature AI can ask every tick. A unit that holds no auras is free.
/// A slow is not a gate: it lowers movement speed through the stat modifiers. Tick thread only.
/// </summary>
public static class ControlGates
{
    /// <summary>The crowd control <paramref name="unit" /> is under; None for a unit that holds no auras.</summary>
    public static CrowdControl Of(IUnit unit) => AuraHolders.Of(unit)?.Control ?? CrowdControl.None;

    /// <summary>Whether it may step: not while stunned or rooted.</summary>
    public static bool CanMove(IUnit unit) => (Of(unit) & (CrowdControl.Stun | CrowdControl.Root)) == 0;

    /// <summary>Whether it may change its orientation: not while stunned (a rooted unit still turns).</summary>
    public static bool CanTurn(IUnit unit) => (Of(unit) & CrowdControl.Stun) == 0;

    /// <summary>
    /// Whether it may act on the world other than by casting: use items, doodads, gear, loot, NPCs and portals. Not
    /// while stunned.
    /// </summary>
    public static bool CanAct(IUnit unit) => (Of(unit) & CrowdControl.Stun) == 0;

    /// <summary>
    /// Whether it may cast: not while stunned, and not while silenced unless <paramref name="basic" /> (the basic
    /// attack is not silenced).
    /// </summary>
    public static bool CanCast(IUnit unit, bool basic)
    {
        CrowdControl control = Of(unit);
        return (control & CrowdControl.Stun) == 0 && (basic || (control & CrowdControl.Silence) == 0);
    }

    /// <summary>
    /// Whether <paramref name="ability" /> is <paramref name="caster" />'s basic attack, the one a silence spares: a
    /// class basic (<see cref="AbilityFlags.Basic" /> on its row), or a creature's own basic clone.
    /// </summary>
    public static bool IsBasic(IUnit caster, IAbility ability) =>
        (ability.Metadata.Flags & AbilityFlags.Basic) != 0 || caster is Creature creature && creature.Abilities.IsBasic(ability);
}
