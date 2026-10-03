using System.Diagnostics.CodeAnalysis;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Auras;
using Avalon.Network.Packets.Social;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Auras;

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

    // Reused by every update: the units holding an aura, and the auras of the unit being ticked, walked as a copy so
    // that an aura ending (a death included) never changes the list under the walk.
    private readonly List<IUnit> _holders = [];
    private readonly List<ActiveAura> _working = [];

    // A tick that throws, logged at most once per ThrottledErrorLog.Interval for each aura; a unit's pass that throws
    // outside its ticks (a stats refresh while its auras end), likewise for each unit.
    private readonly Dictionary<uint, ThrottledErrorLog> _tickFailures = [];
    private readonly Dictionary<ObjectGuid, ThrottledErrorLog> _unitFailures = [];

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
    public AuraApplyResult Apply(IUnit? caster, IUnit target, AuraId aura, AuraSource source) =>
        TryGetLoaded(aura, target, out AuraTemplate? template)
            ? Apply(caster, target, template, source)
            : AuraApplyResult.Refused;

    /// <summary>
    /// The loaded aura <paramref name="aura" /> from the current catalog; false, logged at most once per
    /// ThrottledErrorLog.Interval for each aura, when the catalog does not hold it (nothing was applied to
    /// <paramref name="target" />).
    /// </summary>
    public bool TryGetLoaded(AuraId aura, IUnit target, [NotNullWhen(true)] out AuraTemplate? template)
    {
        if (_catalog().TryGet(aura, out template))
            return true;

        if (ShouldLogRefusal("not loaded", aura.Value))
            _logger.LogWarning("Aura {AuraId} is not loaded; nothing was applied to {Unit}", aura.Value, target.Guid);
        return false;
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

        if (template.Kind == AuraKind.Harmful && (CombatService.IgnoresHits(target) || IsWalkingHome(target)))
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
        AuraSnapshot snapshot = AuraRules.Snapshot(template, caster is null ? default : CombatService.AttackerFor(caster),
            _combat.Random);
        AuraApplyResult result = Place(target, auras, held, template, casterGuid, source, snapshot);

        // A first hook (or the removal it asked for) that killed the target: death ends every aura, and a corpse enters
        // no combat.
        if (IsDead(target))
        {
            RemoveAll(target, AuraRemoveReason.Death);
            return result;
        }

        if (template.Kind == AuraKind.Harmful && caster is not null)
            _combat.EnterAuraCombat(caster, target);

        return result;
    }

    /// <summary>
    /// Renews the copy held (stacking it when its row allows) or adds a new one, refreshes the stats it modifies and runs
    /// its script's OnStack or OnApply; a script that ended its aura from that hook has it removed at once.
    /// </summary>
    private AuraApplyResult Place(IUnit target, UnitAuras auras, ActiveAura? held, AuraTemplate template,
        ObjectGuid casterGuid, AuraSource source, AuraSnapshot snapshot)
    {
        DateTimeOffset now = _time.GetUtcNow();
        AuraApplyResult result;
        if (held is not null)
        {
            uint stacks = AuraRules.NextStacks(template.Stacking, held.Stacks, template.MaxStacks);
            bool stacked = stacks > held.Stacks;
            held.Renew(template, casterGuid, source, stacks, snapshot, now);
            auras.Changed(held, stacked ? AuraChangeKind.Stacked : AuraChangeKind.Refreshed, now);
            if (stacked)
            {
                RefreshStats(target, template);
                RunHook(target, held, "OnStack", static (s, c) => s.OnStack(c));
            }

            result = stacked ? AuraApplyResult.Stacked : AuraApplyResult.Refreshed;
        }
        else
        {
            held = new ActiveAura(template, casterGuid, source, 1, snapshot,
                AuraSchedule.Start(now, template.DurationMs, template.TickIntervalMs), template.DurationMs, now.UtcDateTime);
            auras.Add(held, now);
            RefreshStats(target, template);
            RunHook(target, held, "OnApply", static (s, c) => s.OnApply(c));
            result = AuraApplyResult.Applied;
        }

        // A script may end its aura from its first hook.
        if (held.ScriptEnded && auras.Contains(held))
            Remove(target, held, AuraRemoveReason.Script);

        return result;
    }

    /// <summary>
    /// One pass, right after the instance's ability cast system: for each unit holding an aura (characters, then
    /// creatures), a dead unit loses them all; a creature walking home loses its harmful ones; then each aura, in the
    /// order applied, expires if its template is no longer loaded, takes every tick it is owed at the clock's now (a late
    /// pass catches up, never more than the aura has left), ends if its script asked, and expires once its time is up,
    /// after its last tick. A tick that kills its unit ends every aura it holds and ticks nothing more on it. After a
    /// stall, each aura's owed ticks are paid together, aura by aura in the order they were applied, not interleaved by
    /// their times (the balance simulator ticks every frame and never catches up, so the two agree). A tick that throws
    /// is logged (throttled per aura) and keeps the fraction it carried; the pass goes on with the next aura and the
    /// next unit. Anything else in a unit's pass that throws (a stats refresh as its auras end on death or the walk home)
    /// is logged (throttled per unit) and the pass goes on with the next unit. Allocates nothing while no unit holds an aura.
    /// </summary>
    public void Update()
    {
        if (!CollectHolders())
            return;

        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            AuraCatalog catalog = _catalog();
            for (int i = 0; i < _holders.Count; i++)
            {
                IUnit unit = _holders[i];
                try
                {
                    UpdateUnit(unit, now, catalog);
                }
                catch (Exception e)
                {
                    UnitFailed(unit, e);
                }
            }
        }
        finally
        {
            _holders.Clear();
        }
    }

    /// <summary>
    /// After a stretch with no players, when the instance's pass did not run: no tick owed meanwhile is paid. An aura whose
    /// end has passed ends (Expired, its script told); every other one keeps its end and is left owing only the ticks at
    /// now or later, as a resumed aura is (<see cref="AuraSchedule.Resume" />: a tick that falls on now is kept), with its
    /// carried fraction. Time is not stopped: a stretch longer than an aura's remaining time ends it. Call it before the
    /// first pass after the stretch. Contained per unit, as the pass is.
    /// </summary>
    public void SkipOwed()
    {
        if (!CollectHolders())
            return;

        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            for (int i = 0; i < _holders.Count; i++)
            {
                IUnit unit = _holders[i];
                try
                {
                    SkipOwed(unit, now);
                }
                catch (Exception e)
                {
                    UnitFailed(unit, e);
                }
            }
        }
        finally
        {
            _holders.Clear();
        }
    }

    private void SkipOwed(IUnit unit, DateTimeOffset now)
    {
        UnitAuras auras = AuraHolders.Of(unit)!;
        _working.Clear();
        _working.AddRange(auras.All);
        try
        {
            foreach (ActiveAura aura in _working)
            {
                if (!auras.Contains(aura))
                    continue;

                AuraSchedule schedule = aura.Schedule;
                if (now > schedule.ExpiresAt)
                {
                    Remove(unit, aura, AuraRemoveReason.Expired);

                    // Its script's OnRemove may have killed the unit: death ends the rest.
                    if (!EndIfDead(unit))
                        return;
                    continue;
                }

                uint remainingMs = (uint)Math.Min(uint.MaxValue, Math.Floor(schedule.Remaining(now).TotalMilliseconds));
                uint intervalMs = (uint)Math.Min(uint.MaxValue, schedule.Interval.TotalMilliseconds);
                aura.Schedule = schedule with
                {
                    TicksLeft = AuraSchedule.Resume(now, remainingMs, intervalMs, schedule.TicksLeft).TicksLeft,
                };
            }
        }
        finally
        {
            _working.Clear();
        }
    }

    /// <summary>Fills the reused holder list with every unit here holding an aura; false when none does.</summary>
    private bool CollectHolders()
    {
        _holders.Clear();
        foreach (ICharacter character in _characters.Values)
        {
            if (character is CharacterEntity { Auras.Count: > 0 } entity)
                _holders.Add(entity);
        }

        foreach (ICreature creature in _creatures.Values)
        {
            if (creature is Creature { Auras.Count: > 0 } held)
                _holders.Add(held);
        }

        return _holders.Count > 0;
    }

    private void UpdateUnit(IUnit unit, DateTimeOffset now, AuraCatalog catalog)
    {
        UnitAuras auras = AuraHolders.Of(unit)!;
        if (IsDead(unit))
        {
            RemoveAll(unit, AuraRemoveReason.Death);
            return;
        }

        if (IsWalkingHome(unit))
            RemoveHarmful(unit, AuraRemoveReason.Reset);

        _working.Clear();
        _working.AddRange(auras.All);
        try
        {
            foreach (ActiveAura aura in _working)
            {
                if (!auras.Contains(aura))
                    continue;

                try
                {
                    if (!UpdateAura(unit, auras, aura, now, catalog))
                        break;
                }
                catch (Exception e)
                {
                    TickFailed(aura, e);
                }
            }
        }
        finally
        {
            _working.Clear();
        }
    }

    /// <summary>One aura's pass; false once its unit is dead, so nothing more on it ticks.</summary>
    private bool UpdateAura(IUnit unit, UnitAuras auras, ActiveAura aura, DateTimeOffset now, AuraCatalog catalog)
    {
        if (!catalog.TryGet(aura.Id, out _))
        {
            Remove(unit, aura, AuraRemoveReason.Expired);
            return EndIfDead(unit);
        }

        int due = aura.Schedule.Due(now);
        for (int k = 0; k < due; k++)
        {
            aura.Schedule = aura.Schedule.AfterTicks(1);
            Tick(unit, aura, lastTick: aura.Schedule.TicksLeft == 0);
            if (IsDead(unit) || !auras.Contains(aura) || aura.ScriptEnded)
                break;
        }

        // A killing tick ends every aura and ticks nothing more on the unit; removing is idempotent, so a death
        // already reported for it is no matter.
        if (IsDead(unit))
        {
            RemoveAll(unit, AuraRemoveReason.Death);
            return false;
        }

        if (!auras.Contains(aura))
            return true;

        if (aura.ScriptEnded)
            Remove(unit, aura, AuraRemoveReason.Script);
        else if (aura.Schedule.Expired(now))
            Remove(unit, aura, AuraRemoveReason.Expired);
        return EndIfDead(unit);
    }

    /// <summary>
    /// After an aura ended: its script's OnRemove may have killed the unit, in which case every aura it holds ends and
    /// nothing more ticks on the corpse. False once the unit is dead.
    /// </summary>
    private bool EndIfDead(IUnit unit)
    {
        if (!IsDead(unit))
            return true;

        RemoveAll(unit, AuraRemoveReason.Death);
        return false;
    }

    private void UnitFailed(IUnit unit, Exception e)
    {
        if (!_unitFailures.TryGetValue(unit.Guid, out ThrottledErrorLog? log))
            _unitFailures[unit.Guid] = log = new ThrottledErrorLog(_logger, _time, $"auras of {unit.Guid}");
        log.Failed(e);
    }

    private void TickFailed(ActiveAura aura, Exception e)
    {
        if (!_tickFailures.TryGetValue(aura.Id.Value, out ThrottledErrorLog? log))
            _tickFailures[aura.Id.Value] = log = new ThrottledErrorLog(_logger, _time, $"aura {aura.Id.Value} tick");
        log.Failed(e);
    }

    /// <summary>
    /// One tick: its damage or heal, from the caster if it is still here, otherwise from nobody, with the copy's own
    /// fraction of a point carried in and out; its <paramref name="lastTick" /> rounds what is left.
    /// </summary>
    private void Tick(IUnit unit, ActiveAura aura, bool lastTick)
    {
        var hit = new PeriodicHit(CasterHere(aura.CasterGuid), unit, aura.Id,
            aura.Snapshot.PerTickPerStack * aura.Stacks, aura.Snapshot, aura.Source);

        double carry = aura.PeriodicCarry;
        try
        {
            switch (aura.Template.PeriodicKind)
            {
                case AuraPeriodicKind.Damage:
                    _combat.ApplyPeriodicDamage(hit, ref carry, lastTick);
                    break;
                case AuraPeriodicKind.Heal:
                    _combat.ApplyPeriodicHeal(hit, ref carry, lastTick);
                    break;
            }
        }
        finally
        {
            // Written back even when the tick threw after taking its points, so they are never dealt twice.
            aura.PeriodicCarry = carry;
        }

        RunHook(unit, aura, "OnTick", static (s, c) => s.OnTick(c));
    }

    /// <summary>
    /// Runs one hook of the aura's script, contained by the script host; nothing without a script host or a script. A
    /// hook may end its aura (ScriptEnded) or deal damage that kills; the caller checks both once the hook returns.
    /// </summary>
    private void RunHook(IUnit unit, ActiveAura aura, string hook, Action<AuraScript, IAuraContext> call)
    {
        if (_scripts is null || string.IsNullOrWhiteSpace(aura.Template.ScriptName))
            return;

        var context = new AuraContext(this, unit, aura, _time.GetUtcNow());
        try
        {
            _scripts.Run(aura.Template, hook, script => call(script, context));
        }
        finally
        {
            // A script that keeps its context cannot act through it once the hook has returned.
            context.End();
        }
    }

    /// <summary>
    /// A script's hit from its aura, as a single tick: the snapshot's crit and the target's armour, the caster credited
    /// only while it is here (<see cref="CasterHere" />).
    /// </summary>
    internal uint ScriptDamage(IUnit target, ActiveAura aura, uint amount) =>
        _combat.ApplyPeriodicDamage(new PeriodicHit(CasterHere(aura.CasterGuid), target, aura.Id, amount, aura.Snapshot,
            aura.Source));

    /// <summary>A script's fixed heal, from the caster while it is here, else from the target itself.</summary>
    internal uint ScriptHeal(IUnit target, ActiveAura aura, uint amount) =>
        _combat.RestoreHealth(CasterHere(aura.CasterGuid) ?? target, target, amount);

    /// <summary>A system line to the target's connection in this instance, if it has one.</summary>
    internal void Tell(IUnit target, string line)
    {
        if (_connectionOf(target.Guid) is { } connection)
            connection.Send(SChatMessagePacket.System(line, _time.GetUtcNow().UtcDateTime, connection.CryptoSession.Encrypt));
    }

    /// <summary>The caster, only while it is alive in this instance: an aura outlives its caster, but its credit does not.</summary>
    internal IUnit? CasterHere(ObjectGuid guid)
    {
        if (guid.RawValue == 0)
            return null;

        if (_characters.TryGetValue(guid, out ICharacter? character))
            return character.IsDead ? null : character;

        if (_creatures.TryGetValue(guid, out ICreature? creature))
            return creature.CurrentHealth == 0 ? null : creature;

        return null;
    }

    /// <summary>
    /// A creature walking home after a fight, by this system's one answer to it (the combat script's own, unless one was
    /// given): Apply refuses it a harmful aura, and a tick ends the harmful auras it holds.
    /// </summary>
    private bool IsWalkingHome(IUnit unit) => unit is ICreature && _returningHome(unit);

    /// <summary>
    /// Ends one aura on <paramref name="unit" />, for <paramref name="reason" />, then tells its script. Nothing if it no
    /// longer holds it, so its script hears of it once however often removal is reached (a hook that kills while its
    /// aura ends included).
    /// </summary>
    public void Remove(IUnit unit, ActiveAura aura, AuraRemoveReason reason)
    {
        if (AuraHolders.Of(unit) is not { } auras || !auras.Remove(aura, _time.GetUtcNow()))
            return;

        try
        {
            RefreshStats(unit, aura.Template);
        }
        finally
        {
            // The aura is gone either way: its script hears of it even when the stats refresh threw.
            RunHook(unit, aura, "OnRemove", (s, c) => s.OnRemove(c, reason));
        }
    }

    /// <summary>
    /// The owner's cancel (auras), in this order: a dead unit cancels nothing; one holding no copy of the aura (with
    /// <paramref name="instanceKey" /> when given, matched as given, so 0 names no copy) is told so; a harmful aura cannot
    /// be cancelled; otherwise the named copy ends, or every copy it holds when no key was given, reason Cancelled.
    /// </summary>
    public AuraCancelResult Cancel(IUnit unit, AuraId aura, uint? instanceKey = null)
    {
        if (IsDead(unit))
            return AuraCancelResult.Dead;

        if (AuraHolders.Of(unit) is not { } auras)
            return AuraCancelResult.NotFound;

        ActiveAura[] copies = auras.All
            .Where(a => a.Id.Value == aura.Value && (instanceKey is not { } key || a.Key == key))
            .ToArray();
        if (copies.Length == 0)
            return AuraCancelResult.NotFound;

        if (copies.Any(a => a.Template.Kind != AuraKind.Helpful))
            return AuraCancelResult.NotCancellable;

        foreach (ActiveAura copy in copies)
            Remove(unit, copy, AuraRemoveReason.Cancelled);

        return AuraCancelResult.Ok;
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
        if (AuraHolders.Of(unit) is not { Count: > 0 } auras || !HoldsHarmful(auras))
            return;

        foreach (ActiveAura aura in auras.All.Where(a => a.Template.Kind == AuraKind.Harmful).ToArray())
            Remove(unit, aura, reason);
    }

    private static bool HoldsHarmful(UnitAuras auras)
    {
        IReadOnlyList<ActiveAura> all = auras.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Template.Kind == AuraKind.Harmful)
                return true;
        }

        return false;
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
