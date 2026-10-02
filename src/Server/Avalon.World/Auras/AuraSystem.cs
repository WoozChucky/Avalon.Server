using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Combat;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Auras;

/// <summary>What applying an aura did.</summary>
public enum AuraApplyResult
{
    /// <summary>
    /// Nothing: the target is dead, ignores hits (a harmful aura), holds none, is at the cap, or the aura is not loaded
    /// or no longer fits the ability that applies it.
    /// </summary>
    Refused,
    Applied,
    Refreshed,
    Stacked,
}

/// <summary>
/// The auras of one instance's units (auras): applies them (by their row's stacking, up to Game:MaxAurasPerUnit a unit),
/// ticks them right after the ability cast system on the instance's clock, removes them, refreshes the stats they
/// modify, and runs their scripts. One per MapInstance; it keeps no state of its own beyond reused lists and its log
/// throttle, since every aura lives on its unit, so an aura goes wherever its unit goes. Tick thread only. World-side.
/// </summary>
public sealed class AuraSystem
{
    private readonly CombatService _combat;
    private readonly Dictionary<ObjectGuid, ICharacter> _characters;
    private readonly Dictionary<ObjectGuid, ICreature> _creatures;
    private readonly Func<AuraCatalog> _catalog;
    private readonly Func<StaticData> _data;
    private readonly TimeProvider _time;
    private readonly int _maxPerUnit;
    private readonly ILogger _logger;
    private readonly AuraScripts? _scripts;
    private readonly Func<ObjectGuid, IWorldConnection?> _connectionOf;
    private readonly Func<IUnit, bool> _returningHome;

    // When each refusal was last logged, by what was refused, so one that repeats every cast cannot flood the log.
    private readonly Dictionary<(string Why, uint AuraId), DateTimeOffset> _refusalsLogged = [];

    /// <param name="characters">The instance's characters, the dictionary itself, so a tick walks it without allocating.</param>
    /// <param name="creatures">The instance's creatures, likewise.</param>
    /// <param name="catalog">The current aura catalog (StaticData.Auras), read once per update and once per application.</param>
    /// <param name="data">The current reference data, for a character's stats refresh.</param>
    /// <param name="time">The instance's clock.</param>
    /// <param name="scripts">Runs the auras' scripts; none runs without it.</param>
    /// <param name="connectionOf">A character's connection in this instance, for a script's Tell; none without it.</param>
    /// <param name="returningHome">Whether a creature is walking home after a fight; the combat script's own answer by default.</param>
    public AuraSystem(CombatService combat, Dictionary<ObjectGuid, ICharacter> characters,
        Dictionary<ObjectGuid, ICreature> creatures, Func<AuraCatalog> catalog, Func<StaticData> data, TimeProvider time,
        int maxAurasPerUnit, ILogger logger, AuraScripts? scripts = null,
        Func<ObjectGuid, IWorldConnection?>? connectionOf = null, Func<IUnit, bool>? returningHome = null)
    {
        _combat = combat;
        _characters = characters;
        _creatures = creatures;
        _catalog = catalog;
        _data = data;
        _time = time;
        _maxPerUnit = maxAurasPerUnit;
        _logger = logger;
        _scripts = scripts;
        _connectionOf = connectionOf ?? (static _ => null);
        _returningHome = returningHome ?? (static unit => unit is ICreature { Script: IReturningHome { IsReturningHome: true } });
    }

    public AuraCatalog Catalog => _catalog();

    /// <summary>
    /// Applies the loaded aura <paramref name="aura" />, looked up in the current catalog now; Refused (and logged)
    /// when the catalog does not hold it, as after a reload that dropped it.
    /// </summary>
    public AuraApplyResult Apply(IUnit? caster, IUnit target, AuraId aura, AuraSource source)
    {
        if (!_catalog().TryGet(aura, out AuraTemplate? template))
        {
            if (ShouldLogRefusal("not loaded", aura.Value))
                _logger.LogWarning("Aura {AuraId} is not loaded; nothing was applied to {Unit}", aura.Value, target.Guid);
            return AuraApplyResult.Refused;
        }

        return Apply(caster, target, template, source);
    }

    /// <summary>
    /// Applies <paramref name="template" /> to <paramref name="target" /> from <paramref name="caster" /> (null for
    /// nobody). Who may receive it is the caller's to decide (an ability's shape and Hostility, an item's party rule);
    /// this refuses only an aura that no longer fits the ability that applies it (a reload of either can make them
    /// diverge), a dead target, a harmful aura on a target every hit passes over, and a new aura past the cap. The
    /// snapshot comes from the caster's stats now, its base damage roll (if the row has a coefficient) drawn from the
    /// combat service's random. A harmful aura from a caster enters combat.
    /// </summary>
    public AuraApplyResult Apply(IUnit? caster, IUnit target, AuraTemplate template, AuraSource source)
    {
        if (source.Affects is { } affects && AuraRules.LinkProblem(affects, template.Id, _ => template) is { } misfit)
        {
            if (ShouldLogRefusal("misfit", template.Id.Value))
                _logger.LogWarning("Ability {AbilityId} applies no aura: {Problem}", source.AbilityId?.Value, misfit);
            return AuraApplyResult.Refused;
        }

        if (AuraHolders.Of(target) is not { } auras || IsDead(target))
            return AuraApplyResult.Refused;

        if (template.Kind == AuraKind.Harmful && CombatService.IgnoresHits(target))
            return AuraApplyResult.Refused;

        // A copy keyed by caster only for Independent rows: Renew overwrites the caster of every other kind.
        ObjectGuid casterGuid = caster?.Guid ?? new ObjectGuid();
        ActiveAura? held = auras.Find(template.Id, AuraRules.KeysByCaster(template.Stacking) ? casterGuid : null);

        // The cap counts new auras only: a copy the unit holds still refreshes or stacks.
        if (held is null && auras.Count >= _maxPerUnit)
        {
            if (ShouldLogRefusal("cap", template.Id.Value))
            {
                _logger.LogWarning("Refused aura {AuraId} on {Unit}: it already holds {Count}, the most Game:MaxAurasPerUnit allows",
                    template.Id.Value, target.Guid, auras.Count);
            }

            return AuraApplyResult.Refused;
        }

        // Rolled once, here, past every refusal, so a refused aura draws nothing from the combat random.
        DateTimeOffset now = _time.GetUtcNow();
        AuraSnapshot snapshot = AuraRules.Snapshot(template, caster is null ? default : CombatService.AttackerFor(caster),
            _combat.Random);

        AuraApplyResult result;
        if (held is not null)
        {
            uint stacks = AuraRules.NextStacks(template.Stacking, held.Stacks, template.MaxStacks);
            bool stacked = stacks > held.Stacks;
            held.Renew(template, casterGuid, source, stacks, snapshot, now);
            auras.Changed(held, stacked ? AuraChangeKind.Stacked : AuraChangeKind.Refreshed, now);
            if (stacked)
                RefreshStats(target, template);
            result = stacked ? AuraApplyResult.Stacked : AuraApplyResult.Refreshed;
        }
        else
        {
            held = new ActiveAura(template, casterGuid, source, 1, snapshot,
                AuraSchedule.Start(now, template.DurationMs, template.TickIntervalMs), template.DurationMs, now.UtcDateTime);
            auras.Add(held, now);
            RefreshStats(target, template);
            result = AuraApplyResult.Applied;
        }

        if (template.Kind == AuraKind.Harmful && caster is not null)
            _combat.EnterAuraCombat(caster, target);

        return result;
    }

    /// <summary>Ends one aura on <paramref name="unit" />, for <paramref name="reason" />. Nothing if it no longer holds it.</summary>
    public void Remove(IUnit unit, ActiveAura aura, AuraRemoveReason reason)
    {
        if (AuraHolders.Of(unit) is not { } auras || !auras.Remove(aura, _time.GetUtcNow()))
            return;

        RefreshStats(unit, aura.Template);
    }

    /// <summary>Ends every aura on <paramref name="unit" />: death does this.</summary>
    public void RemoveAll(IUnit unit, AuraRemoveReason reason)
    {
        if (AuraHolders.Of(unit) is not { Count: > 0 } auras)
            return;

        foreach (ActiveAura aura in auras.All.ToArray())
            Remove(unit, aura, reason);
    }

    /// <summary>Ends every harmful aura on <paramref name="unit" />, keeping its helpful ones.</summary>
    public void RemoveHarmful(IUnit unit, AuraRemoveReason reason)
    {
        if (AuraHolders.Of(unit) is not { Count: > 0 } auras)
            return;

        foreach (ActiveAura aura in auras.All.Where(a => a.Template.Kind == AuraKind.Harmful).ToArray())
            Remove(unit, aura, reason);
    }

    /// <summary>A character is dead by its flag, anything else at 0 health: the rule combat uses.</summary>
    public static bool IsDead(IUnit unit) => unit is ICharacter character ? character.IsDead : unit.CurrentHealth == 0;

    /// <summary>
    /// After an aura with stat modifiers changed: a character's stats are refreshed over the current reference data,
    /// a creature folds its auras.
    /// </summary>
    private void RefreshStats(IUnit unit, AuraTemplate template)
    {
        if (template.Modifiers.Count > 0)
            AuraStatsRefresh.Apply(unit, _data());
    }

    /// <summary>Whether a refusal is logged now: at most once per ThrottledErrorLog.Interval for each reason and aura.</summary>
    private bool ShouldLogRefusal(string why, uint auraId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_refusalsLogged.TryGetValue((why, auraId), out DateTimeOffset last) && now - last < ThrottledErrorLog.Interval)
            return false;

        _refusalsLogged[(why, auraId)] = now;
        return true;
    }
}
