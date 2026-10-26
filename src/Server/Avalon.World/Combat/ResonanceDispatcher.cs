using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.World.Abilities.Targeting;
using Avalon.World.Auras;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

/// <summary>
/// Fires a character's active Resonance effects (#1180 part A2) when one of their triggers happens to it: one per
/// instance, on its tick. A wearer whose effects were built from an earlier effect catalog than the world's current one
/// (an Auras or Items reload since its last stats refresh) has them rebuilt from the current one first. Each matching
/// effect, in slot order, checks its wearer's life threshold, then its cooldown (against the time the event read), then
/// its target, then its chance, then spends the cooldown and applies its aura through the instance's aura system with the
/// effect's ready-made source; a chaining effect also applies it to the nearest living hostile creatures around its
/// target. A miss spends nothing; a refused or resisted placement has already spent it. Builds nothing per firing, and
/// reads no clock: a placement allocates what the aura system's own does.
/// </summary>
public sealed class ResonanceDispatcher
{
    /// <summary>How far from the struck target a chaining effect looks for more targets, in metres.</summary>
    public const float ChainRadiusMetres = 8f;

    private readonly AuraSystem _auras;
    private readonly IHitQuery _hits;
    private readonly ICombatRandom _random;
    private readonly MapType _mapType;
    private readonly ICombatOutcomes _outcomes;
    private readonly Func<ResonanceEffectCatalog?> _effects;

    /// <param name="auras">The instance's aura system, which places every effect's aura.</param>
    /// <param name="hits">The instance's hit query, which finds a chain's further targets.</param>
    /// <param name="random">The combat random each chance is drawn from.</param>
    /// <param name="mapType">The instance's map type, which hostility reads.</param>
    /// <param name="outcomes">Where a resisted crowd control is reported: the instance.</param>
    /// <param name="effects">
    /// The world's current effect catalog (StaticData.ResonanceEffects), read once per firing; null for a world without
    /// reference data, whose wearers keep the effects they were built with.
    /// </param>
    internal ResonanceDispatcher(AuraSystem auras, IHitQuery hits, ICombatRandom random, MapType mapType,
        ICombatOutcomes outcomes, Func<ResonanceEffectCatalog?> effects)
    {
        _auras = auras;
        _hits = hits;
        _random = random;
        _mapType = mapType;
        _outcomes = outcomes;
        _effects = effects;
    }

    /// <summary>
    /// <paramref name="fired" /> happened to <paramref name="wearer" /> at <paramref name="now" />, with
    /// <paramref name="other" /> at its other end: the unit it hit (Hit, Crit), the creature it killed (Kill), or the unit
    /// that struck at it (Struck, Evaded, WardBroken). Nothing for a dead wearer. An effect on another unit fires only on
    /// one hostile to the wearer, and one that chains also from a dead target, where its target takes nothing; any other on
    /// a dead target draws nothing. A placement whose stats refresh rebuilt the wearer's effects (the first refresh after
    /// an Items reload recounts) ends the walk: the slots after it are no longer the ones it was walking.
    /// </summary>
    internal void Fire(CharacterEntity wearer, ResonanceTriggers fired, IUnit other, DateTime now)
    {
        if (wearer.IsDead)
            return;

        ResonanceProcs procs = wearer.ResonanceProcs;
        if (_effects() is { } current && !ReferenceEquals(procs.BuiltFrom, current))
            procs.Rebuild(wearer.Resonance, current);

        int built = procs.Builds;
        for (int i = 0; i < procs.Count; i++)
        {
            ResonanceEffect effect = procs.EffectAt(i);
            if ((effect.Bit & fired) == 0)
                continue;

            // Above the threshold, or on cooldown, nothing is drawn or spent; a cooldown costs a compare.
            if (effect.AtOrBelowLifePct is { } pct && wearer.CurrentHealth > wearer.Health * (double)pct / 100d)
                continue;
            if (now < procs.ReadyAt(i))
                continue;

            IUnit target = effect.Target == ResonanceTarget.Wearer ? wearer : other;
            bool chainOnly = false;
            if (!ReferenceEquals(target, wearer))
            {
                if (!Hostility.IsHostile(wearer, target, _mapType))
                    continue;

                if (AuraSystem.IsDead(target))
                {
                    if (effect.ChainTargets == 0)
                        continue;

                    chainOnly = true;
                }
            }

            // A miss spends nothing; an effect at 100 % draws nothing.
            if (effect.ChancePct < 100f && _random.NextDouble() * 100d >= effect.ChancePct)
                continue;

            procs.Spend(i, now + effect.Cooldown);
            if (!chainOnly)
                Place(wearer, target, effect);
            if (effect.ChainTargets > 0)
                Chain(wearer, other, effect);

            if (procs.Builds != built)
                return;
        }
    }

    /// <summary>
    /// The effect's aura on <paramref name="unit" />; a crowd control it resists whole is told as Immune. True when it
    /// landed: applied, refreshed or stacked, never refused or resisted.
    /// </summary>
    private bool Place(CharacterEntity wearer, IUnit unit, ResonanceEffect effect)
    {
        AuraApplyResult result = _auras.Apply(wearer, unit, effect.Aura, effect.Source);
        if (result == AuraApplyResult.Resisted)
            _outcomes.ControlResisted(wearer, unit, null);
        return result is not (AuraApplyResult.Refused or AuraApplyResult.Resisted);
    }

    /// <summary>
    /// The effect's aura on the nearest living creatures hostile to the wearer within <see cref="ChainRadiusMetres" /> of
    /// <paramref name="from" />, until as many as it chains to took it, never <paramref name="from" /> itself: one that
    /// refuses or resists it does not count, and the jump goes on to the next. The query's list is lent for the tick and
    /// walked by index, so nothing is allocated once its pooled lists are warm.
    /// </summary>
    private void Chain(CharacterEntity wearer, IUnit from, ResonanceEffect effect)
    {
        IReadOnlyList<IUnit> near = _hits.InCircle(_hits.PositionOf(from), ChainRadiusMetres);
        int landed = 0;
        for (int i = 0; i < near.Count && landed < effect.ChainTargets; i++)
        {
            IUnit unit = near[i];
            if (unit is not ICreature || ReferenceEquals(unit, from) || unit.CurrentHealth == 0
                || !Hostility.IsHostile(wearer, unit, _mapType))
            {
                continue;
            }

            if (Place(wearer, unit, effect))
                landed++;
        }
    }
}
