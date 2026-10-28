using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Auras;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Auras;

/// <summary>
/// The auras of one instance's units (auras): applies them (by their row's stacking, up to Game:MaxAurasPerUnit a unit),
/// ticks them right after the ability cast system on the instance's clock, removes them, refreshes the stats they
/// modify, and runs their scripts. One per MapInstance; every aura lives on its unit, so an aura goes wherever its unit
/// goes, and beyond reused lists and its log throttles it keeps only its <see cref="Roster" /> of which units here hold
/// an aura or changed. Tick thread only. World-side.
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
    private readonly TimeSpan _diminishWindow;
    private readonly ILogger _logger;
    private readonly AuraScripts? _scripts;
    private readonly Func<ObjectGuid, IWorldConnection?> _connectionOf;
    private readonly Func<IUnit, bool> _returningHome;
    private readonly Action<IUnit, CrowdControl>? _controlled;

    // The types a character's PvP diminishing counts (#770), walked by index; a slow never diminishes.
    private static readonly CrowdControl[] s_diminished = [CrowdControl.Stun, CrowdControl.Root, CrowdControl.Silence];

    // When each refusal was last logged, by what was refused, so one that repeats every cast cannot flood the log.
    private readonly Dictionary<(string Why, uint AuraId), DateTimeOffset> _refusalsLogged = [];

    // Which units here hold an aura, and which changed this tick (#898): kept as they change, never found by a walk.
    private readonly AuraRoster _roster;

    // Reused by every update: the units holding an aura, and the auras of the unit being ticked, walked as a copy so
    // that an aura ending (a death included) never changes the list under the walk.
    private readonly List<IUnit> _holders = [];
    private readonly List<ActiveAura> _working = [];

    // The copies the removal paths walk (the owner's cancel, death, a creature's reset), kept and emptied after each
    // use so that ending auras allocates no copy (#883). One list per depth: ending an aura runs its script's OnRemove,
    // which may kill the unit and so end the rest of its auras from inside the walk that is ending them.
    private readonly List<List<ActiveAura>> _ending = [];
    private int _endingDepth;

    // The pool of copies (#1180 part A2): every copy this system made (ActiveAura.Pool, its token) that is removed from a
    // unit here waits in _removed until the next pass starts, when no walk, hook or caller of the tick it was removed in can still
    // be reading it, and then joins _free, its references let go, which every new aura placed here is taken from before
    // one is made. So a placement allocates nothing once the pool holds as many copies as this instance's units shed
    // between two passes, and a copy is never reused while anything that saw it removed is still running. A copy made
    // elsewhere (by another instance, or restored at login) is left to the collector, so the pool never holds more copies
    // than this instance itself had made, its own peak: a long-lived town ending copies its visitors bring keeps none.
    private readonly List<ActiveAura> _removed = [];
    private readonly Stack<ActiveAura> _free = new();

    // What each copy made here names as its pool (ActiveAura.Pool): an empty object of this system's own, so a copy that
    // outlives this instance elsewhere holds no reference to it.
    private readonly object _poolToken = new();

    // A tick that throws, logged at most once per ThrottledErrorLog.Interval for each aura; a unit's pass that throws
    // outside its ticks (a stats refresh while its auras end), likewise for each unit.
    private readonly Dictionary<uint, ThrottledErrorLog> _tickFailures = [];
    private readonly Dictionary<ObjectGuid, ThrottledErrorLog> _unitFailures = [];

    /// <param name="characters">The instance's characters, the dictionary itself, where a caster is looked up.</param>
    /// <param name="creatures">The instance's creatures, likewise. Who holds an aura is the <see cref="Roster" />'s.</param>
    /// <param name="catalog">The current aura catalog (StaticData.Auras), read once per update and once per application.</param>
    /// <param name="data">The current reference data, for a character's stats refresh.</param>
    /// <param name="time">The instance's clock.</param>
    /// <param name="maxAurasPerUnit">Game:MaxAurasPerUnit: the most auras one unit holds.</param>
    /// <param name="diminishWindow">
    /// Game:CrowdControlDiminishWindow (#770): how long after the last stun, root or silence from a character that
    /// landed on another the next of that type still diminishes.
    /// </param>
    /// <param name="logger">Where refusals and failures are logged.</param>
    /// <param name="scripts">Runs the auras' scripts; none runs without it.</param>
    /// <param name="connectionOf">A character's connection in this instance, for a script's Tell; none without it.</param>
    /// <param name="returningHome">Whether a creature is walking home after a fight; the combat script's own answer by default.</param>
    /// <param name="controlled">
    /// Told when a stun or a silence lands on a unit (#770), with what landed, after the aura is placed: its instance
    /// ends the unit's running cast bar. Nothing without it.
    /// </param>
    public AuraSystem(CombatService combat, Dictionary<ObjectGuid, ICharacter> characters,
        Dictionary<ObjectGuid, ICreature> creatures, Func<AuraCatalog> catalog, Func<StaticData> data, TimeProvider time,
        int maxAurasPerUnit, TimeSpan diminishWindow, ILogger logger, AuraScripts? scripts = null,
        Func<ObjectGuid, IWorldConnection?>? connectionOf = null, Func<IUnit, bool>? returningHome = null,
        Action<IUnit, CrowdControl>? controlled = null)
    {
        _roster = new AuraRoster(this);
        _combat = combat;
        _characters = characters;
        _creatures = creatures;
        _catalog = catalog;
        _data = data;
        _time = time;
        _maxPerUnit = maxAurasPerUnit;
        _diminishWindow = diminishWindow;
        _logger = logger;
        _scripts = scripts;
        _connectionOf = connectionOf ?? (static _ => null);
        _returningHome = returningHome ?? (static unit => unit is ICreature { Script: IReturningHome { IsReturningHome: true } });
        _controlled = controlled;
    }

    public AuraCatalog Catalog => _catalog();

    /// <summary>
    /// The units of this instance holding an aura and those whose auras changed this tick. Its instance joins each unit
    /// as it becomes a member and has it leave as it goes; the auras keep it from there.
    /// </summary>
    public AuraRoster Roster => _roster;

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
    /// <remarks>
    /// A crowd-control aura (#770: one that stuns, roots or silences, or a slow) may land for less than its row's
    /// duration: on a creature, the share its <see cref="Creature.ControlResistance" /> takes; on a character, from
    /// another character, the PvP diminishing step of each type it stuns, roots or silences with (full, half, quarter,
    /// immune), the smallest winning, each type then recorded as landed. Nothing landing (a share of 0, the immune step,
    /// or a creature walking home) is Resisted: nothing is placed and nothing recorded. Its refresh keeps the later of
    /// the held expiry and the new one. A stun or a silence that landed then ends the target's running cast bar, through
    /// the instance. A <paramref name="durationMs" /> given (a GameMaster's /aura, #1152) is the duration placed exactly,
    /// a refresh included: no resistance share, no diminishing step, nothing recorded.
    /// <para>
    /// #1181 Part D: the source ability's capstone factors, read from its metadata before the aura lets it go. Its
    /// <see cref="Avalon.World.Public.Abilities.AbilityMetadata.AuraDurationPct" /> lengthens the row's duration
    /// (<see cref="AuraRules.Lengthen" />), so a periodic aura holds the extra ticks that time gives, each tick the row's
    /// own, and the total grows; its <see cref="Avalon.World.Public.Abilities.AbilityMetadata.AuraStacksBonus" /> raises
    /// a Stack aura's cap for this application (<see cref="AuraRules.NextStacks" />), and no application lowers the stacks
    /// a copy holds. Neither ever reaches a crowd-control aura: the talent catalog refuses them there, and one edited into
    /// crowd control by a reload after the tree loaded is placed for its own (scaled) duration with no bonus. A given
    /// <paramref name="durationMs" /> is placed as given, never lengthened.
    /// </para>
    /// </remarks>
    public AuraApplyResult Apply(IUnit? caster, IUnit target, AuraTemplate template, AuraSource source,
        uint? durationMs = null)
    {
        // The fit alone: the row is in hand, and a lookup delegate over it cost every application a closure, made on
        // entry and so before any refusal, and a delegate with it for an ability's aura.
        if (source.Affects is { } affects && AuraRules.FitProblem(affects, template) is { } misfit)
        {
            if (ShouldLogRefusal("misfit", template.Id.Value))
            {
                // A source with no ability (#1180: a Resonance effect's) is named by its aura, never as an ability.
                if (source.AbilityId is { } ability)
                {
                    _logger.LogWarning("Ability {AbilityId} applies no aura: {Problem}", ability.Value, misfit);
                }
                else
                {
                    _logger.LogWarning("Aura {AuraId} '{AuraName}' is not applied: it is {Kind}, which its source cannot place on a {Affects} target",
                        template.Id.Value, template.Name, template.Kind, affects);
                }
            }

            return AuraApplyResult.Refused;
        }

        if (AuraHolders.Of(target) is not { } auras || IsDead(target))
            return AuraApplyResult.Refused;

        // A crowd-control aura (#770): one that grants a stun, root or silence, or a slow; only a harmful aura can.
        bool controls = false;
        if (template.Kind == AuraKind.Harmful)
        {
            controls = template.Control != CrowdControl.None || AuraRules.IsSlow(template);

            // A creature walking home resists crowd control (its caster is told "Immune") and refuses any other harmful
            // aura; one every hit passes over for any other reason (arrival protection, invulnerable) refuses either.
            if (CombatService.IgnoresHits(target) || IsWalkingHome(target))
            {
                return controls && IsWalkingHome(target) && target is not ICreature { Invulnerable: true }
                    ? AuraApplyResult.Resisted
                    : AuraApplyResult.Refused;
            }
        }

        // A copy keyed by caster only for Independent rows: Renew overwrites the caster of every other kind.
        ObjectGuid casterGuid = caster?.Guid ?? new ObjectGuid();
        ActiveAura? held = auras.Find(template.Id, AuraRules.KeysByCaster(template.Stacking) ? casterGuid : null);

        // The cap counts new auras only: a copy the unit holds still refreshes or stacks.
        bool reservesArrivalSlot = ArrivalProtection.Is(template.Id.Value) && auras.HeldArrivalProtection is null;
        if (held is null && !reservesArrivalSlot && auras.OrdinaryCount >= _maxPerUnit)
        {
            if (ShouldLogRefusal("cap", template.Id.Value))
            {
                _logger.LogWarning("Refused aura {AuraId} on {Unit}: it already holds {Count}, the most Game:MaxAurasPerUnit allows",
                    template.Id.Value, target.Guid, auras.OrdinaryCount);
            }

            return AuraApplyResult.Refused;
        }

        // Past the cap, so an aura refused there is no diminishing step (#770).
        DateTimeOffset now = _time.GetUtcNow();
        bool scaled = controls && durationMs is null;
        uint placedMs = durationMs ?? template.DurationMs;
        uint stackBonus = 0;
        if (scaled)
        {
            placedMs = ControlledDuration(caster, target, template, now);
            if (placedMs == 0)
                return AuraApplyResult.Resisted;
        }
        else if (!controls && source.Ability is { } shaped)
        {
            // #1181 Part D: a capstone's longer, deeper aura, never crowd control's.
            stackBonus = shaped.AuraStacksBonus;
            if (durationMs is null)
                placedMs = AuraRules.Lengthen(template.DurationMs, shaped.AuraDurationPct);
        }

        // Rolled once, here, past every refusal, so a refused aura draws nothing from the combat random.
        // #1178: its ticks take the source ability's type, else the caster's base type, else Physical; its total comes from
        // the source ability and the caster's offence as they are now, and nothing that happens to the caster later
        // changes it. With no caster there is no source to draw on.
        AttackerCombat casterCombat = caster is null ? default : CombatService.AttackerFor(caster);
        AuraSnapshot snapshot = AuraRules.Snapshot(template, casterCombat, caster is null ? null : source.Ability,
            HitResolver.TypeOf(source.DamageType, casterCombat), _combat.Random, source.CannotCrit);
        // The metadata has done its work: the aura keeps its source without it, so it holds no ability for its life.
        AuraApplyResult result = Place(target, auras, held, template, casterGuid, source with { Ability = null }, snapshot,
            placedMs, scaled, stackBonus, now);

        // A first hook (or the removal it asked for) that killed the target: death ends every aura, and a corpse enters
        // no combat.
        if (IsDead(target))
        {
            RemoveAll(target, AuraRemoveReason.Death);
            return result;
        }

        if (template.Kind == AuraKind.Harmful && caster is not null)
            _combat.EnterAuraCombat(caster, target);

        // A stun or a silence it still holds from this aura (a first hook may have ended it) ends its running cast bar.
        CrowdControl interrupting = template.Control & auras.Control & (CrowdControl.Stun | CrowdControl.Silence);
        if (interrupting != CrowdControl.None)
            _controlled?.Invoke(target, interrupting);

        return result;
    }

    /// <summary>
    /// How long a crowd-control aura lands for on <paramref name="target" /> (#770), 0 for not at all: a creature's
    /// resistance share of the row's duration; a character's PvP diminishing step when <paramref name="caster" /> is a
    /// character and the aura stuns, roots or silences (each such type recorded as landed when it lands, so its window
    /// runs from now); the full duration otherwise (a creature's or nobody's on a character, a slow alone).
    /// </summary>
    private uint ControlledDuration(IUnit? caster, IUnit target, AuraTemplate template, DateTimeOffset now)
    {
        if (target is Creature creature)
            return CrowdControlRules.Scale(template.DurationMs, creature.ControlResistance.PctOf(template));

        if (target is not CharacterEntity character || caster is not ICharacter || template.Control == CrowdControl.None)
            return template.DurationMs;

        ControlDiminishing diminishing = character.Diminishing;
        float pct = 100f;
        for (int i = 0; i < s_diminished.Length; i++)
        {
            CrowdControl type = s_diminished[i];
            if ((template.Control & type) != 0)
                pct = MathF.Min(pct, CrowdControlRules.DiminishPct(diminishing.Landed(type, now, _diminishWindow)));
        }

        // The immune step lands nothing and is not recorded, so the window still runs from the last that landed.
        uint durationMs = CrowdControlRules.Scale(template.DurationMs, pct);
        if (durationMs == 0)
            return 0;

        for (int i = 0; i < s_diminished.Length; i++)
        {
            CrowdControl type = s_diminished[i];
            if ((template.Control & type) != 0)
                diminishing.Record(type, now);
        }

        return durationMs;
    }

    /// <summary>
    /// Renews the copy held (stacking it when its row allows) or adds a new one, for <paramref name="durationMs" />,
    /// refreshes the stats it modifies and runs its script's OnStack or OnApply; a script that ended its aura from that
    /// hook has it removed at once. A crowd-control aura's refresh (<paramref name="controls" />) never shortens what the
    /// held copy has left (#770): it keeps the later expiry. <paramref name="stackBonus" /> raises a Stack row's cap for
    /// this application (#1181 Part D), and the stacks held are never lowered.
    /// </summary>
    private AuraApplyResult Place(IUnit target, UnitAuras auras, ActiveAura? held, AuraTemplate template,
        ObjectGuid casterGuid, AuraSource source, AuraSnapshot snapshot, uint durationMs, bool controls,
        uint stackBonus, DateTimeOffset now)
    {
        AuraApplyResult result;
        if (held is not null)
        {
            uint stacks = AuraRules.NextStacks(template.Stacking, held.Stacks, template.MaxStacks, stackBonus);
            bool stacked = stacks > held.Stacks;
            if (controls)
                durationMs = Math.Max(durationMs, UnitAuras.RemainingMs(held, now));
            held.Renew(template, casterGuid, source, stacks, snapshot, durationMs, now);
            auras.Changed(held, stacked ? AuraChangeKind.Stacked : AuraChangeKind.Refreshed, now);
            if (stacked)
            {
                RefreshStats(target, template);
                RunHook(target, held, "OnStack", static (s, c, _) => s.OnStack(c));
            }

            result = stacked ? AuraApplyResult.Stacked : AuraApplyResult.Refreshed;
        }
        else
        {
            held = Take(template, casterGuid, source, snapshot, AuraSchedule.Start(now, durationMs, template.TickIntervalMs),
                durationMs, now.UtcDateTime);
            auras.Add(held, now);
            RefreshStats(target, template);
            RunHook(target, held, "OnApply", static (s, c, _) => s.OnApply(c));
            result = AuraApplyResult.Applied;
        }

        // A script may end its aura from its first hook.
        if (held.ScriptEnded && auras.Contains(held))
            Remove(target, held, AuraRemoveReason.Script);

        return result;
    }

    /// <summary>A new copy of one stack: a pooled one placed again when the pool has one, else a new one.</summary>
    private ActiveAura Take(AuraTemplate template, ObjectGuid casterGuid, AuraSource source, AuraSnapshot snapshot,
        AuraSchedule schedule, uint durationMs, DateTime appliedAt)
    {
        if (!_free.TryPop(out ActiveAura? aura))
        {
            return new ActiveAura(template, casterGuid, source, 1, snapshot, schedule, durationMs, appliedAt)
            {
                Pool = _poolToken,
            };
        }

        aura.Reuse(template, casterGuid, source, 1, snapshot, schedule, durationMs, appliedAt);
        return aura;
    }

    /// <summary>
    /// The copies removed since the last pass join the pool, each letting go of what it referenced: called as a pass
    /// starts, outside every walk and hook, so nothing still reads one of them.
    /// </summary>
    private void PoolRemoved()
    {
        for (int i = 0; i < _removed.Count; i++)
        {
            ActiveAura aura = _removed[i];
            aura.Clear();
            _free.Push(aura);
        }

        _removed.Clear();
    }

    /// <summary>
    /// One pass, right after the instance's ability cast system: for each unit holding an aura as the pass starts
    /// (characters, then creatures, each in the order the <see cref="Roster" /> keeps), a dead unit loses them all; a creature walking home loses its harmful ones; then each aura, in the
    /// order applied, expires if its template is no longer loaded, takes every tick it is owed at the clock's now (a late
    /// pass catches up, never more than the aura has left), ends if its script asked, breaks by chance once per whole
    /// second held when its row gives it a chance to (#770, Broken), and expires once its time is up, after its last
    /// tick. A tick that kills its unit ends every aura it holds and ticks nothing more on it. After a stall, each
    /// aura's owed ticks (and rolls to break) are paid together, aura by aura in the order they were applied, not
    /// interleaved by their times (the balance simulator ticks every frame and never catches up, so the two agree). A
    /// tick that throws is logged (throttled per aura) and keeps the fraction it carried; the pass goes on with the next
    /// aura and the next unit. Anything else in a unit's pass that throws (a stats refresh as its auras end on death or
    /// the walk home) is logged (throttled per unit) and the pass goes on with the next unit. Allocates nothing while no
    /// unit holds an aura.
    /// </summary>
    public void Update()
    {
        PoolRemoved();
        if (!_roster.CollectHolders(_holders))
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
    /// carried fraction, and only the rolls to break on its own at now or later (#770). Time is not stopped: a stretch
    /// longer than an aura's remaining time ends it. Call it before the first pass after the stretch. Contained per
    /// unit, as the pass is.
    /// </summary>
    public void SkipOwed()
    {
        if (!_roster.CollectHolders(_holders))
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
                aura.SkipBreakRollsBefore(now);
            }
        }
        finally
        {
            _working.Clear();
        }
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
            Tick(unit, auras, aura, lastTick: aura.Schedule.TicksLeft == 0);
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

        // An aura with no chance to break on its own (most) costs one field read here, never a look at its roll time.
        if (aura.ScriptEnded)
            Remove(unit, aura, AuraRemoveReason.Script);
        else if (aura.Template.BreakPerSecondPct > 0f && BreaksOverTime(aura, now))
            Remove(unit, aura, AuraRemoveReason.Broken);
        else if (aura.Schedule.Expired(now))
            Remove(unit, aura, AuraRemoveReason.Expired);
        return EndIfDead(unit);
    }

    /// <summary>
    /// Rolls an aura's chance to break on its own (#770) once for each whole second it has been held up to
    /// <paramref name="now" /> (a late pass rolls every second it owes), never for one at or past its end; true at the
    /// first roll that breaks it, the rest left unrolled.
    /// </summary>
    private bool BreaksOverTime(ActiveAura aura, DateTimeOffset now)
    {
        float pct = aura.Template.BreakPerSecondPct;
        while (aura.NextBreakRollAt <= now && aura.NextBreakRollAt < aura.Schedule.ExpiresAt)
        {
            aura.NextBreakRollAt += CrowdControlRules.BreakRollInterval;
            if (CrowdControlRules.Breaks(pct, _combat.Random))
                return true;
        }

        return false;
    }

    /// <summary>
    /// A hit or a tick dealt more than 0 to <paramref name="unit" />, which lives on (#770), reported once by the combat
    /// service through its instance: each aura it holds with a chance to break on damage rolls it once, in the order
    /// held, and each that breaks ends (Broken, its script told). Those that break are copied first and ended after the
    /// walk, so an ending (whose script may deal damage and so come back here) never changes the list under it. Draws
    /// nothing, allocates nothing and walks nothing when no aura it holds has such a chance
    /// (<see cref="UnitAuras.BreaksOnDamage" />).
    /// </summary>
    public void DamageTaken(IUnit unit)
    {
        if (AuraHolders.Of(unit) is not { BreaksOnDamage: true } auras)
            return;

        List<ActiveAura>? broken = null;
        try
        {
            IReadOnlyList<ActiveAura> all = auras.All;
            for (int i = 0; i < all.Count; i++)
            {
                ActiveAura held = all[i];
                if (CrowdControlRules.Breaks(held.Template.BreakOnDamagePct, _combat.Random))
                    (broken ??= RentEnding()).Add(held);
            }

            if (broken is not null)
                EndEach(unit, broken, AuraRemoveReason.Broken);
        }
        finally
        {
            if (broken is not null)
                ReturnEnding(broken);
        }
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
    /// fraction of a point carried in and out; its <paramref name="lastTick" /> rounds what is left. Its script hears
    /// OnTick only while <paramref name="auras" /> still holds it: a tick that ended its own aura (its unit killed, or
    /// the aura broken by the tick's damage, #770) has already told the script OnRemove.
    /// </summary>
    private void Tick(IUnit unit, UnitAuras auras, ActiveAura aura, bool lastTick)
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

        if (auras.Contains(aura))
            RunHook(unit, aura, "OnTick", static (s, c, _) => s.OnTick(c));
    }

    /// <summary>
    /// Runs one hook of the aura's script, contained by the script host; nothing without a script host or a script. A
    /// hook may end its aura (ScriptEnded) or deal damage that kills; the caller checks both once the hook returns.
    /// <paramref name="reason" /> is for OnRemove, handed to <paramref name="call" /> so that it captures nothing.
    /// </summary>
    private void RunHook(IUnit unit, ActiveAura aura, string hook, Action<AuraScript, IAuraContext, AuraRemoveReason> call,
        AuraRemoveReason reason = default)
    {
        if (_scripts is null || string.IsNullOrWhiteSpace(aura.Template.ScriptName))
            return;

        RunScriptHook(_scripts, unit, aura, hook, call, reason);
    }

    /// <summary>
    /// The hook of an aura that has a script. It allocates the context and nothing else (#883): the hook, the context
    /// and the reason go to the script host as one value, where a lambda over them was a closure and a delegate for
    /// every hook. The context stays an object of its own for each hook, because it is ended when the hook returns: a
    /// script that kept one could act through it again if the next hook were handed the same object.
    /// </summary>
    private void RunScriptHook(AuraScripts scripts, IUnit unit, ActiveAura aura, string hook,
        Action<AuraScript, IAuraContext, AuraRemoveReason> call, AuraRemoveReason reason)
    {
        var context = new AuraContext(this, unit, aura, _time.GetUtcNow());
        try
        {
            scripts.Run(aura.Template, hook, (Call: call, Context: context, Reason: reason),
                static (script, hook) => hook.Call(script, hook.Context, hook.Reason));
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
            connection.Send(SChatMessagePacket.System(line, _time.GetUtcNow().UtcDateTime, PacketEncoder.Shared));
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
    /// aura ends included). A copy this system made goes back to its pool once, here, and is placed again no sooner than
    /// after the next pass starts; one made elsewhere is left to the collector. The caller may still read it until its own
    /// call returns, but must not keep it.
    /// </summary>
    public void Remove(IUnit unit, ActiveAura aura, AuraRemoveReason reason)
    {
        if (AuraHolders.Of(unit) is not { } auras || !auras.Remove(aura, _time.GetUtcNow()))
            return;

        if (ReferenceEquals(aura.Pool, _poolToken))
        {
            Debug.Assert(!aura.Returned, "An aura removed from its unit was already given back to the pool");
            aura.Returned = true;
            _removed.Add(aura);
        }

        try
        {
            RefreshStats(unit, aura.Template);
        }
        finally
        {
            // The aura is gone either way: its script hears of it even when the stats refresh threw.
            RunHook(unit, aura, "OnRemove", static (s, c, reason) => s.OnRemove(c, reason), reason);
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

        List<ActiveAura> copies = RentEnding();
        try
        {
            IReadOnlyList<ActiveAura> all = auras.All;
            for (int i = 0; i < all.Count; i++)
            {
                ActiveAura held = all[i];
                if (held.Id.Value == aura.Value && (instanceKey is not { } key || held.Key == key))
                    copies.Add(held);
            }

            if (copies.Count == 0)
                return AuraCancelResult.NotFound;

            for (int i = 0; i < copies.Count; i++)
            {
                if (copies[i].Template.Kind != AuraKind.Helpful)
                    return AuraCancelResult.NotCancellable;
            }

            EndEach(unit, copies, AuraRemoveReason.Cancelled);
            return AuraCancelResult.Ok;
        }
        finally
        {
            ReturnEnding(copies);
        }
    }

    /// <summary>Ends every held copy of an aura through the normal removal hooks, regardless of its kind.</summary>
    public void Remove(IUnit unit, AuraId id, AuraRemoveReason reason)
    {
        if (AuraHolders.Of(unit) is not { Count: > 0 } auras)
            return;

        List<ActiveAura> copies = RentEnding();
        try
        {
            IReadOnlyList<ActiveAura> all = auras.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id.Value == id.Value)
                    copies.Add(all[i]);
            }

            EndEach(unit, copies, reason);
        }
        finally
        {
            ReturnEnding(copies);
        }
    }

    /// <summary>Ends every aura on <paramref name="unit" />: death does this.</summary>
    public void RemoveAll(IUnit unit, AuraRemoveReason reason)
    {
        if (AuraHolders.Of(unit) is not { Count: > 0 } auras)
            return;

        List<ActiveAura> held = RentEnding();
        try
        {
            held.AddRange(auras.All);
            EndEach(unit, held, reason);
        }
        finally
        {
            ReturnEnding(held);
        }
    }

    /// <summary>Ends every harmful aura on <paramref name="unit" />, keeping its helpful ones.</summary>
    public void RemoveHarmful(IUnit unit, AuraRemoveReason reason)
    {
        if (AuraHolders.Of(unit) is not { Count: > 0 } auras || !HoldsHarmful(auras))
            return;

        List<ActiveAura> harmful = RentEnding();
        try
        {
            IReadOnlyList<ActiveAura> all = auras.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Template.Kind == AuraKind.Harmful)
                    harmful.Add(all[i]);
            }

            EndEach(unit, harmful, reason);
        }
        finally
        {
            ReturnEnding(harmful);
        }
    }

    /// <summary>
    /// Ends the auras copied into <paramref name="ending" />, in the order held. A copy, since each one ended leaves the
    /// unit's own list; one that is gone by its turn (ended from inside an earlier one's hook) is passed over by Remove.
    /// </summary>
    private void EndEach(IUnit unit, List<ActiveAura> ending, AuraRemoveReason reason)
    {
        for (int i = 0; i < ending.Count; i++)
            Remove(unit, ending[i], reason);
    }

    /// <summary>An empty list for one removal path's copy, at the depth it runs at; <see cref="ReturnEnding" /> gives it back.</summary>
    private List<ActiveAura> RentEnding()
    {
        if (_endingDepth == _ending.Count)
            _ending.Add([]);

        List<ActiveAura> list = _ending[_endingDepth++];
        Debug.Assert(list.Count == 0, "A list of auras to end still holds an earlier removal's");
        return list;
    }

    /// <summary>Empties the list, so it keeps no aura (and through it no unit) alive, and frees its depth.</summary>
    private void ReturnEnding(List<ActiveAura> list)
    {
        list.Clear();
        _endingDepth--;
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
            AuraStatsRefresh.Apply(unit, _data(), _time.GetUtcNow().UtcDateTime);
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
