using System.Diagnostics;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abilities;
using Avalon.World.Auras;
using Avalon.World.Combat;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abilities;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Abilities;

public interface IAbilityCastSystem
{
    /// <summary>
    /// Takes a cast-time cast aimed at <paramref name="aim" />, pays its cost, marks it casting and broadcasts its
    /// start, with its cast id and the footprint it will land on (#648). False, with nothing spent, nothing sent
    /// and <c>Casting</c> left clear, when its script is missing or cannot be built, or the cost cannot be paid.
    /// The script is built now, with this aim and the caster's position now as its origin, and fired once the
    /// cast time has run out. Any unit may cast (#163); a creature's casts cost nothing.
    /// A <paramref name="free" /> cast (an item's, item use) neither checks nor pays the cost. A cast taken ends its
    /// caster's arrival protection (#1088); a refused one does not. One <paramref name="forItem" /> was started by an
    /// item (item use) and is marked so (<see cref="AbilityInstance.ForItem" />): a silence spares it (#770). False too
    /// for a stunned caster, and a silenced one unless the cast is its basic attack or an item's (#770).
    /// </summary>
    bool QueueAbility(IUnit caster, AbilityAim aim, IAbility ability, bool free = false, bool forItem = false);

    /// <summary>
    /// Instant-cast counterpart to <see cref="QueueAbility" />: builds the script, pays the cost, starts
    /// the cooldown, broadcasts the finish-cast, runs <c>Prepare()</c>, and keeps the script ticking if it
    /// has not finished (a projectile). False, with nothing spent, when the script cannot be built or
    /// the cost cannot be paid.
    /// A <paramref name="free" /> cast (an item's, item use) neither checks nor pays the cost. A cast taken ends its
    /// caster's arrival protection before it fires (#1088).
    /// <paramref name="view" /> is how far its hits are rewound (#1082): a player's cast through the instance's
    /// <c>IPlayerCastHost</c>; the default, none, for every other. Refused under crowd control as a queued cast is, a
    /// <paramref name="forItem" /> cast (an item's, item use) spared by a silence (#770).
    /// </summary>
    bool RunInstant(IUnit caster, AbilityAim aim, IAbility ability, bool free = false, CastView view = default,
        bool forItem = false);

    /// <summary>
    /// One tick: the dodge wait's hits that are due land, or are dodged (#1082), then the queued casts run down, fire or
    /// are interrupted, then the running scripts tick.
    /// </summary>
    void Update(TimeSpan deltaTime, List<IWorldObject> objects);

    /// <summary>
    /// Drops every finished script at once, its final state sent or not. For an instance nobody is in
    /// (#164): there is nobody to send that state to, and a player who entered later would otherwise
    /// see a projectile frozen where it stopped.
    /// </summary>
    void DropFinished();

    /// <summary>
    /// Interrupts every queued cast of <paramref name="caster" />, as moving does: its timers reset,
    /// <c>Casting</c> clears, the interrupt is sent, nothing fires and nothing is refunded. For a caster
    /// leaving the instance (#164): an instance nobody is in is not ticked, so its queue would never
    /// clear the cast, and the caster could cast nothing anywhere else. Every dodge-wait hit with the unit as its caster
    /// or its target is dropped too (#1082): a caster who is gone credits nothing here, a target who is gone takes nothing.
    /// </summary>
    void CancelCasts(IUnit caster);

    /// <summary>
    /// Ends the running cast bar of <paramref name="caster" /> (#770: a stun, or a silence, landed on it): each of its
    /// queued casts still winding up resets and clears <c>Casting</c>, and its interrupt is sent; nothing fires, nothing
    /// is refunded and no cooldown starts. With <paramref name="spareBasic" /> (a silence) its basic attack's cast goes
    /// on, and so does a cast an item started (an item use). Unlike <see cref="CancelCasts" />, a hit it already fired
    /// and held for the dodge wait still lands, and its running scripts (a projectile in flight) go on. Safe from inside
    /// a cast firing this tick (a stun dealt by one): a cast that already fired is left alone, and the ones it ends leave
    /// the queue once the tick's walk is over.
    /// </summary>
    void Interrupt(IUnit caster, bool spareBasic);

    /// <summary>
    /// Drops every active script <paramref name="caster" /> cast, a projectile in flight included, so
    /// none of them hits, threatens or credits anything again (#541). A dropped projectile is no longer
    /// a world object, so every client still watching it is sent its removal on the next tick, as for
    /// one that finished. For a caster leaving the instance, called with <see cref="CancelCasts" />.
    /// </summary>
    void CancelScriptsOf(IUnit caster);

    IWorldObject? GetAbility(ObjectGuid guid);

    /// <summary>
    /// The next cast id of this instance (#648), never 0. The cast system takes one for every cast it takes; an item's
    /// cast bar takes one too, so every cast in an instance has its own.
    /// </summary>
    uint TakeCastId();
}

public class InstanceAbilityCastSystem(
    ILoggerFactory factory,
    IServiceProvider serviceProvider,
    IScriptManager scriptManager,
    IAbilityArena arena)
    : IAbilityCastSystem
{
    /// <summary>
    /// Up to this many active scripts <see cref="GetAbility" /> walks their list; above it, it answers from an index by
    /// guid (#883). A walk that short is as fast as the index, and an instance that never holds more (one party's
    /// fight) never allocates one. Public for the tests, which put the lookup on its index by firing more.
    /// </summary>
    public const int WalkedUpTo = 8;

    /// <summary>
    /// How many free and retired scripts of each pooled type this instance keeps (<see cref="AbilityScriptPool" />, #1181
    /// Part D), and so how many hit sets, free queued-cast entries and free running-list entries: past it, one handed back
    /// is left to the collector. Public for the tests, which fire past it.
    /// </summary>
    public const int PooledScripts = 256;

    private readonly ActiveScripts _activeAbilities = new();
    private readonly List<ActiveScript> _failed = [];

    // The entries that left the running list in one removal, handed back once the list is whole again (#1181 Part D).
    private readonly List<ActiveScript> _released = [];

    // This instance's pooled scripts, and the free entries of its queue and of its running list (#1181 Part D).
    private readonly AbilityScriptPool _pool = NewPool(arena);
    private readonly Stack<AbilityInstance> _freeCasts = new();
    private readonly Stack<ActiveScript> _freeEntries = new();
    private readonly List<(IUnit Caster, IAbility Ability, uint CastId)> _interrupts = [];
    private readonly ILogger<InstanceAbilityCastSystem> _logger = factory.CreateLogger<InstanceAbilityCastSystem>();
    private readonly HashSet<AbilityInstance> _abilityQueue = [];
    private readonly List<AbilityInstance> _dequeued = [];
    private readonly List<AbilityInstance> _cancelled = [];

    // The casts Interrupt ended (#770), kept between calls. While the queue is being advanced (a stun dealt by a cast that
    // fires in that walk) they cannot leave it: AdvanceQueue takes them out after its walk and sends their interrupts
    // with its own.
    private readonly List<AbilityInstance> _controlled = [];
    private bool _advancing;
    private readonly AbilityScriptActivator _scriptActivator = new();
    private readonly PendingHits _pendingHits = new(factory.CreateLogger<PendingHits>());

    // The world's tick count the dodge wait's hits are due by (#1082), the instance's own; none in a cast system built
    // without one (tests), where no hit ever waits.
    private readonly WorldTicks? _worldTicks = serviceProvider.GetService<WorldTicks>();
    private uint _lastCastId;

    /// <summary>
    /// The dodge wait's hits (#1082): the instance adds to them (<see cref="IAbilityArena.TryDeferHit" />), this resolves
    /// them each tick and drops a leaving unit's. Public for the tests, whose arena holds hits in it.
    /// </summary>
    public PendingHits PendingHits => _pendingHits;

    public bool QueueAbility(IUnit caster, AbilityAim aim, IAbility ability, bool free = false, bool forItem = false)
    {
        if (RefusedForControl(caster, ability, forItem))
            return false;

        bool pays = !free && !IsFree(caster);
        bool fromLife = PaysWithLife(caster);
        if (pays && AbilityCost.Check(caster, ability.Metadata, fromLife) is var cost and not CostCheck.Payable)
        {
            CombatLog.QueuedCastRefused(_logger, cost, ability.AbilityId);
            return false;
        }

        // #648: the cast resolves from where the caster stands now, not where it stands when it fires, so it
        // lands on the footprint its start broadcast carries.
        aim = aim with { Origin = caster.Position };

        // Built now, with the aim captured at cast start, so a script that is missing or cannot be
        // built is refused before anything is paid, and a reload that removes it mid-cast costs nothing. A fan's first
        // lane (#1181 Part D); its other lanes are built when it fires, from the aim kept on the entry.
        if (Build(caster, aim, 0, ability) is not { } script)
        {
            return false;
        }

        // #627, #1178: the pace is read once, here: the cast time and the cooldown the cast sets when it fires are fixed
        // now, so a gear change mid-cast (a weapon swap) changes neither.
        CasterPace pace = PaceOf(caster);
        uint castId = TakeCastId();
        AbilityFootprint? footprint = FootprintOf(caster, aim, ability, LanesOf(script, ability));
        if (!_freeCasts.TryPop(out AbilityInstance? queued))
            queued = new AbilityInstance();
        queued.Start(caster, ability, script, aim, caster.Position, CooldownOf(caster, ability, pace), castId, footprint,
            forItem);
        _abilityQueue.Add(queued);

        // #521 item 1: Casting is set, and the cost paid, only once the queue has taken the cast.
        ability.Casting = true;
        ability.CastTimeTimer = AbilityPace.CastTime(ability.Metadata, pace);
        if (pays)
        {
            AbilityCost.Pay(caster, ability.Metadata, fromLife);
        }

        CombatLog.CastQueued(_logger, ability.AbilityId, caster.Guid, ability.Metadata.CastTime, _abilityQueue.Count);

        // #1088: a cast taken ends its caster's arrival protection, before anything is sent of it.
        ArrivalProtection.End(caster);
        arena.BroadcastUnitStartCast(caster, ability, castId, footprint);
        return true;
    }

    public bool RunInstant(IUnit caster, AbilityAim aim, IAbility ability, bool free = false, CastView view = default,
        bool forItem = false)
    {
        if (RefusedForControl(caster, ability, forItem))
            return false;

        bool pays = !free && !IsFree(caster);
        bool fromLife = PaysWithLife(caster);
        if (pays && AbilityCost.Check(caster, ability.Metadata, fromLife) is var cost and not CostCheck.Payable)
        {
            CombatLog.InstantCastRefused(_logger, cost, ability.AbilityId);
            return false;
        }

        aim = aim with { Origin = caster.Position };
        if (Build(caster, aim, 0, ability) is not { } script)
        {
            return false;
        }

        if (pays)
        {
            AbilityCost.Pay(caster, ability.Metadata, fromLife);
        }

        // #1088: a cast taken ends its caster's arrival protection, before it fires, so it lands as any other caster's.
        ArrivalProtection.End(caster);
        Fire(caster, ability, script, CooldownOf(caster, ability, PaceOf(caster)), TakeCastId(), view, null, null, aim);
        return true;
    }

    public void Update(TimeSpan deltaTime, List<IWorldObject> objects)
    {
        // A script that finished on an earlier tick leaves now, not on the tick it finished (#164): a
        // projectile stays a world object until its final state (where it stopped, zero velocity) has
        // been taken for a broadcast, so every client sees it spawn, stop and despawn, even one that
        // ended on the tick it was first seen. Updates go out only on broadcast ticks, so that can be
        // a few ticks later. First the pool frees the projectiles whose view rounds have passed (#1181 Part D).
        _pool.Recycle();
        SweepFinished(keepUnsent: true);

        // #1082: the dodge wait's hits due now, first, so each is judged after the inputs its target sent this tick (step
        // 2), and before anything fires: a hit is never due on the tick its cast fired.
        _pendingHits.Resolve(_worldTicks?.Completed ?? 0, arena);

        AdvanceQueue(deltaTime);
        SendInterrupts();
        TickScripts(deltaTime, objects);
    }

    /// <summary>
    /// Runs down every queued cast's timer: a creature that died or turned for home is interrupted at once,
    /// a caster who moved is interrupted, a finished cast is fired, or dropped when its caster died. Every
    /// cast that ends leaves the queue after the loop, and its interrupt, if any, is collected for
    /// <see cref="SendInterrupts" />.
    /// </summary>
    private void AdvanceQueue(TimeSpan deltaTime)
    {
        // #521 item 3: nothing is removed from the queue while it is enumerated.
        _dequeued.Clear();
        _interrupts.Clear();

        _advancing = true;
        try
        {
            AdvanceEach(deltaTime);
        }
        finally
        {
            _advancing = false;
        }

        foreach (AbilityInstance cast in _dequeued)
        {
            _abilityQueue.Remove(cast);
        }

        // The casts a stun or a silence dealt in the walk ended (#770); their interrupts are already in _interrupts.
        foreach (AbilityInstance cast in _controlled)
        {
            _abilityQueue.Remove(cast);
        }

        // Out of the queue, every entry goes back (#1181 Part D); their interrupts hold what they need.
        ReleaseCasts(_dequeued);
        ReleaseCasts(_controlled);
        _dequeued.Clear();
        _controlled.Clear();
    }

    /// <summary>
    /// Entries that have left the queue go back to the free stack, each cleared: the script of a cast that never fired goes
    /// back to its pool at once, since it was never prepared; one that fired is the running list's (#1181 Part D).
    /// </summary>
    private void ReleaseCasts(List<AbilityInstance> casts)
    {
        foreach (AbilityInstance cast in casts)
            ReleaseCast(cast);
    }

    private void ReleaseCast(AbilityInstance cast)
    {
        Debug.Assert(!cast.Returned, "A queued cast's entry was handed back twice");
        if (cast.Returned)
            return;

        if (!cast.Fired)
            _pool.Return(cast.Script);

        cast.Returned = true;
        cast.Clear();
        if (_freeCasts.Count < PooledScripts)
            _freeCasts.Push(cast);
    }

    private void AdvanceEach(TimeSpan deltaTime)
    {
        foreach (AbilityInstance cast in _abilityQueue)
        {
            IAbility ability = cast.Ability;

            // Ended earlier in this walk by a stun or a silence a cast that fired dealt (#770): it waits to leave.
            if (cast.Ended)
            {
                continue;
            }

            ability.CastTimeTimer -= (float)deltaTime.TotalSeconds;

            // #163: a creature that died during its wind-up, or turned for home (the leash, a lost target),
            // ends the cast at once and out loud, however much of it was left, so its corpse or its walk home
            // never lands the hit.
            if (IsAbandonedByCreature(cast.Caster))
            {
                CombatLog.CastDroppedCreatureGone(_logger, ability.AbilityId, cast.Caster.Guid);
                ResetCast(ability);
                _interrupts.Add((cast.Caster, ability, cast.CastId));
                _dequeued.Add(cast);
                continue;
            }

            // A creature is never interrupted by moving (#163): its script asks for no movement while it winds
            // up, so anything that moved it was not its own doing (a crowd's separation pushing a stopped agent),
            // and a push must not cancel, and re-queue, its wind-up every tick.
            if (cast.Caster is not ICreature && cast.CastStartPosition != cast.Caster.Position)
            {
                CombatLog.CastInterruptedByMovement(_logger, ability.AbilityId, cast.Caster.Guid);
                ResetCast(ability);
                _interrupts.Add((cast.Caster, ability, cast.CastId));
                _dequeued.Add(cast);
                continue;
            }

            if (ability.CastTimeTimer > 0)
            {
                continue;
            }

            _dequeued.Add(cast);
            ResetCast(ability);

            // A caster who died during the cast casts nothing, and is left free to cast again. The drop
            // is interrupted out loud (#530), as moving does, so every client's cast bar for it ends.
            if (IsDead(cast.Caster))
            {
                CombatLog.CastDroppedCasterDead(_logger, ability.AbilityId, cast.Caster.Guid);
                _interrupts.Add((cast.Caster, ability, cast.CastId));
                continue;
            }

            // A queued cast is never rewound (#1082): its wind-up is the dodge window, and its footprint was fixed at
            // its start. A hostile circle or cone judges the players it hits by their own screens, through the dodge wait; a
            // projectile's flight is its own dodge window. An Ally cast waits for nobody: a heal or a buff is not a threat
            // to step out of, so every ally, its caster included, is hit as it fires. A hostile cast never targets its
            // caster, so the caster needs no exemption of its own.
            AbilityFootprint? dodge = ability.Metadata.Affects != AbilityAffects.Ally
                                      && cast.Footprint is { Shape: AbilityShape.Circle or AbilityShape.Cone } footprint
                ? footprint
                : null;
            cast.Fired = true;
            Fire(cast.Caster, ability, cast.Script, cast.CooldownSeconds, cast.CastId, default, dodge, cast.Footprint,
                cast.Aim);
        }
    }

    private void SendInterrupts()
    {
        // Sent once every interrupted cast is already out of the queue, as CancelCasts does, and
        // contained: a failing send can neither leave a cast queued nor stop the scripts ticking.
        foreach ((IUnit caster, IAbility ability, uint castId) in _interrupts)
        {
            TryInterrupt(caster, ability, castId);
        }
    }

    /// <summary>
    /// Ticks every unfinished script and hands each projectile to <paramref name="objects" />. Each Update
    /// is contained (#530): a script that throws, or runs without a guid, is dropped, and the others still tick.
    /// </summary>
    private void TickScripts(TimeSpan deltaTime, List<IWorldObject> objects)
    {
        _failed.Clear();

        foreach (ActiveScript active in _activeAbilities)
        {
            AbilityScript script = active.Script;

            // #163: a creature's projectile still in flight when it died or turned for home is dropped, like a
            // departed caster's (#541), so no corpse and no creature walking home deals damage, or is put back into
            // an encounter by it. Left out of this tick's world objects, it is removed from every client's view.
            if (IsAbandonedByCreature(active.Caster))
            {
                _failed.Add(active);
                continue;
            }

            // #1082: a player's projectile sweeps the units where that player sees them beside it, rewound by its cast's
            // projectile view for the whole flight, its caster exempt (tested where it stands now). The values found
            // before are put back after, as Fire does, so a cast fired inside the update returns them to the outer one.
            // A creature's or a queued cast's view rewinds nothing.
            int outerRewind = arena.HitRewindTicks;
            IUnit? outerExempt = arena.HitRewindExempt;
            try
            {
                arena.HitRewindTicks = active.View.ProjectileRewindTicks;
                arena.HitRewindExempt = active.Caster;

                // A finished script is never ticked again, so nothing it does is applied twice. Its state is its
                // own code too, and read inside the containment.
                if (script.State is not SpellState.Finished)
                {
                    script.Update(deltaTime);
                }
            }
            catch (Exception e)
            {
                active.Threw = true;
                _failed.Add(active);
                Failed(active.Caster, active.Ability, e, "Update", active.CastId);
                continue;
            }
            finally
            {
                arena.HitRewindTicks = outerRewind;
                arena.HitRewindExempt = outerExempt;
            }

            // A script must carry a guid from Prepare on. One without (its Prepare set none, or its Update took it
            // away) is contained like one that threw: read unchecked here, it would throw out of every tick.
            if (script.Guid is not { } guid)
            {
                active.Threw = true;
                _failed.Add(active);
                Failed(active.Caster, active.Ability,
                    new InvalidOperationException($"{script.GetType().Name} is running without a guid."), "Update",
                    active.CastId);
                continue;
            }

            if (guid.Type == ObjectType.SpellProjectile)
            {
                objects.Add(script);
            }
        }

        // Left out of this tick's world objects, a dropped projectile is removed from every client's
        // view like one that finished. Each entry goes back, and its script to the pool unless it threw (#1181 Part D).
        foreach (ActiveScript failed in _failed)
        {
            _activeAbilities.Remove(failed);
        }

        ReleaseEntries(_failed);
    }

    public void CancelCasts(IUnit caster)
    {
        // #1082: first, as it cannot throw, so no failing send below can leave a hit for a unit that is gone.
        _pendingHits.Drop(caster);

        // Collected first: nothing is removed from the queue while it is enumerated. Into a list of this call's own,
        // kept between calls: it runs for every character and creature that leaves, most of them casting nothing.
        _cancelled.Clear();
        foreach (AbilityInstance cast in _abilityQueue)
        {
            if (ReferenceEquals(cast.Caster, caster))
            {
                _cancelled.Add(cast);
            }
        }

        foreach (AbilityInstance cast in _cancelled)
        {
            _abilityQueue.Remove(cast);
            ResetCast(cast.Ability);
        }

        try
        {
            // Sent once every cast is already cleared, so a failing send cannot leave one stuck.
            foreach (AbilityInstance cast in _cancelled)
            {
                CombatLog.CastCancelledCasterLeft(_logger, cast.Ability.AbilityId, caster.Guid);
                arena.BroadcastInterruptedCast(caster, cast.Ability, cast.CastId);
            }
        }
        finally
        {
            // Every one is out of the queue: its entry goes back, and its unfired script (#1181 Part D). Emptied now: until
            // the next leave it would hold the casts of a caster who is gone, and the caster.
            ReleaseCasts(_cancelled);
            _cancelled.Clear();
        }
    }

    public void Interrupt(IUnit caster, bool spareBasic)
    {
        // Collected first: nothing is removed from the queue while it is enumerated, and this may run inside
        // AdvanceQueue's own walk of it. A cast with Casting clear already fired this tick; one Ended was ended earlier in
        // it. A silence spares the basic attack and an item's cast (an item use).
        int first = _controlled.Count;
        foreach (AbilityInstance cast in _abilityQueue)
        {
            if (!ReferenceEquals(cast.Caster, caster) || cast.Ended || !cast.Ability.Casting
                || spareBasic && (cast.ForItem || ControlGates.IsBasic(caster, cast.Ability)))
            {
                continue;
            }

            ResetCast(cast.Ability);
            cast.Ended = true;
            _controlled.Add(cast);
            CombatLog.CastInterruptedByControl(_logger, cast.Ability.AbilityId, caster.Guid);
        }

        if (_advancing)
        {
            for (int i = first; i < _controlled.Count; i++)
            {
                AbilityInstance cast = _controlled[i];
                _interrupts.Add((cast.Caster, cast.Ability, cast.CastId));
            }

            return;
        }

        try
        {
            for (int i = first; i < _controlled.Count; i++)
            {
                _abilityQueue.Remove(_controlled[i]);
            }

            // Sent once every cast is already out of the queue, each contained, so a failing send cannot leave one stuck.
            for (int i = first; i < _controlled.Count; i++)
            {
                AbilityInstance cast = _controlled[i];
                TryInterrupt(cast.Caster, cast.Ability, cast.CastId);
            }

            // Out of the queue and told: each entry goes back, and its unfired script (#1181 Part D).
            for (int i = first; i < _controlled.Count; i++)
                ReleaseCast(_controlled[i]);
        }
        finally
        {
            _controlled.RemoveRange(first, _controlled.Count - first);
        }
    }

    public void CancelScriptsOf(IUnit caster)
    {
        int dropped = _activeAbilities.RemoveCastBy(caster, _released);
        ReleaseEntries(_released);
        if (dropped > 0)
        {
            CombatLog.ScriptsDroppedCasterLeft(_logger, dropped, caster.Guid);
        }
    }

    public void DropFinished() => SweepFinished(keepUnsent: false);

    /// <summary>
    /// Drops every finished script, except, with <paramref name="keepUnsent" />, a projectile whose final state is
    /// still owed to the clients. A script's state is its own code, so each is asked inside the containment: one
    /// whose state cannot be read is contained as a throwing Update is, logged, dropped and its caster interrupted.
    /// Every script is decided before any is removed, so nothing can throw while the list is being compacted, which
    /// would leave it with a script twice.
    /// </summary>
    private void SweepFinished(bool keepUnsent)
    {
        bool leaving = false;
        foreach (ActiveScript active in _activeAbilities)
        {
            try
            {
                AbilityScript script = active.Script;
                active.Leaving = script.State is SpellState.Finished
                    && (!keepUnsent || script.Guid?.Type != ObjectType.SpellProjectile || !script.HasUnsentChanges);
            }
            catch (Exception e)
            {
                active.Leaving = true;
                active.Threw = true;
                Failed(active.Caster, active.Ability, e, "State", active.CastId);
            }

            leaving |= active.Leaving;
        }

        if (leaving)
        {
            _activeAbilities.RemoveLeaving(_released);
            ReleaseEntries(_released);
        }
    }

    /// <summary>
    /// Running-list entries that have left the list go back to the free stack, each cleared, and the script of each that did
    /// not throw is retired to its pool (#1181 Part D), which takes back only its own. Called once the list is whole again,
    /// and empties <paramref name="entries" />.
    /// </summary>
    private void ReleaseEntries(List<ActiveScript> entries)
    {
        try
        {
            foreach (ActiveScript entry in entries)
            {
                if (!entry.Threw)
                    _pool.Retire(entry.Script);

                entry.Clear();
                if (_freeEntries.Count < PooledScripts)
                    _freeEntries.Push(entry);
            }
        }
        finally
        {
            entries.Clear();
        }
    }

    /// <summary>
    /// The active script of that guid: found by a walk of the active scripts while they are few, and from their index
    /// by guid above <see cref="WalkedUpTo" /> (#883). The state broadcast asks for every spell object each viewer is
    /// sent, so with the walk alone a crowded fight cost viewers times projectiles squared. Neither allocates: a
    /// <c>Find</c> over a lambda on the guid allocated its closure and delegate on every call (#880).
    /// </summary>
    public IWorldObject? GetAbility(ObjectGuid guid) => _activeAbilities.Find(guid);

    private static void ResetCast(IAbility ability)
    {
        ability.Casting = false;
        ability.CastTimeTimer = ability.Metadata.CastTime;
    }

    /// <summary>
    /// The script for lane <paramref name="lane" /> of a cast of <paramref name="ability" /> aimed at
    /// <paramref name="centre" /> (#1181 Part D): a fan's lanes are turned from the centre aim
    /// (<see cref="FanAim.Lane" />) only for a script type that flies fans (<see cref="IProjectileFan" />); any other
    /// fires its one lane along the aim, whatever its row's projectile count. Null, logged, when the script is missing
    /// or cannot be built.
    /// </summary>
    private AbilityScript? Build(IUnit caster, AbilityAim centre, int lane, IAbility ability)
    {
        Type? scriptType = scriptManager.GetAbilityScript(ability.Metadata.ScriptName);
        if (scriptType is null)
        {
            _logger.LogWarning("Ability script {ScriptName} not found", ability.Metadata.ScriptName);
            return null;
        }

        // The type is asked only for a fan, so a cast of one projectile pays nothing for it.
        int lanes = LanesOf(ability.Metadata);
        if (lanes > 1 && !typeof(IProjectileFan).IsAssignableFrom(scriptType))
            lanes = 1;

        AbilityAim aim = FanAim.Lane(centre, lane, lanes);
        try
        {
            // A type the pool keeps, for a World-side ability, comes from the pool (#1181 Part D): a free one bound to this
            // cast, or a new one. Any other answer is built through the activator, unpooled, as before.
            if (ability is GameAbility pooledFrom && _pool.Take(scriptType, pooledFrom, caster, aim) is { } pooled)
                return pooled;

            // The script's own copy: its timers and casting flag are not its caster's. A World-side ability shares
            // its metadata with the copy, which cannot change; any other ability is copied whole, as before.
            IAbility own = ability is GameAbility held ? held.CloneForCast() : ability.Clone();
            return _scriptActivator.Build(serviceProvider, scriptType, own, caster, aim, arena);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to build ability script {ScriptName}", ability.Metadata.ScriptName);
            return null;
        }
    }

    /// <summary>
    /// A caster's pace (#1178): a character's, fixed at its last stats refresh; a World-side creature's, its speeds capped by
    /// the caps it spawned with (#163). Any other caster casts with no speed and no attack time.
    /// </summary>
    private static CasterPace PaceOf(IUnit caster) => caster switch
    {
        CharacterEntity character => character.Pace,
        Creature creature => creature.Pace,
        _ => default,
    };

    /// <summary>Creatures and staff characters in god mode do not pay ability costs.</summary>
    private static bool IsFree(IUnit caster) => caster is ICreature or CharacterEntity { GodMode: true };

    /// <summary>A character holding Blood Price (#1181 Part D) checks and pays its costs from current life.</summary>
    private static bool PaysWithLife(IUnit caster) => caster is CharacterEntity { PaysWithLife: true };

    /// <summary>
    /// #770, the choke point every cast passes, whoever sends it: a stunned caster casts nothing, an item's cast included;
    /// a silenced one only its basic attack (<see cref="ControlGates.IsBasic" />) or a cast an item started
    /// (<paramref name="forItem" />, an item use). The handlers and creature AI refuse these first; a cast refused here
    /// spends and starts nothing. One cached field read for a caster under no crowd control.
    /// </summary>
    private bool RefusedForControl(IUnit caster, IAbility ability, bool forItem)
    {
        CrowdControl control = ControlGates.Of(caster);
        if (control == CrowdControl.None)
            return false;

        if (ControlGates.CanCast(caster, forItem || ControlGates.IsBasic(caster, ability)))
            return false;

        CombatLog.ControlledCastRefused(_logger, control, ability.AbilityId, caster.Guid);
        return true;
    }

    /// <summary>A character is dead by its flag, a creature at 0 health.</summary>
    private static bool IsDead(IUnit caster) => caster switch
    {
        ICharacter character => character.IsDead,
        ICreature creature => creature.CurrentHealth == 0,
        _ => false,
    };

    /// <summary>
    /// A creature caster that died, or that its combat script is walking home (#163, #610): its queued casts, its
    /// projectiles and its held hits (<see cref="Abilities.PendingHits" />, #1082) land nothing.
    /// </summary>
    internal static bool IsAbandonedByCreature(IUnit caster) =>
        caster is ICreature creature
        && (creature.CurrentHealth == 0 || creature.Script is IReturningHome { IsReturningHome: true });

    /// <summary>
    /// The cooldown a cast sets when it fires, by <see cref="AbilityPace.Cooldown" /> over the pace the cast was read with
    /// (#1178): a creature's basic waits its SwingInterval (#163), a character's basic attack its weapon's attack time over
    /// attack speed, a spell basic its row over cast speed, every other its row over cooldown recovery.
    /// </summary>
    private static float CooldownOf(IUnit caster, IAbility ability, in CasterPace pace) =>
        AbilityPace.Cooldown(ability.Metadata, pace,
            creatureBasic: caster is Creature creature && creature.Abilities.IsBasic(ability));

    /// <summary>
    /// The cooldown, set once (#627), so a later gear change never rescales a running one; the finish-cast
    /// broadcast; the effect. A script still running keeps ticking. Its <c>Prepare</c> runs with the arena's hit queries
    /// rewound by <paramref name="view" /> (#1082), its caster exempt (tested where it stands now), the values found
    /// before put back after, so a cast fired inside another cast's hit returns the outer one to its own. A queued hostile
    /// circle or cone fires with its <paramref name="dodge" /> footprint set on the arena (#1082), so the players it hits
    /// wait out their delay; every other cast with none. It is put back after as the rewind is.
    /// </summary>
    /// <remarks>
    /// <b>A fan (#1181 Part D, <c>AddedProjectiles</c>).</b> A projectile cast whose shape fires more than one projectile
    /// fires <paramref name="script" />, built on lane 0 (<see cref="FanAim.Lane" />), then lanes 1 to n − 1 from
    /// <paramref name="aim" />, the centre, each a projectile of its own taken from the pool as any cast's is, built and
    /// added in a loop: no list per cast, and a warm fan allocates nothing. The count and every lane's shape are the cast's
    /// own copy's (<see cref="PooledAbilityScript.CastCopy" />), never the caster's ability, which a capstone may have
    /// reshaped while the cast wound up: a cast fires the shape it was taken with. Each lane joins the first's set of hit
    /// units (<see cref="IProjectileFan.ShareHitsWith" />) before it is prepared, is prepared in a containment of its own
    /// as the first is, under the cast's id and view, and runs beside it. A fan's start footprint, resolved along the
    /// aim (which no lane flies when the count is even), is not set on the arena, so every lane resolves and tells its
    /// own. A lane that cannot be built is skipped; one that cannot join goes back to the pool unprepared, holding no
    /// join; one whose <c>Prepare</c> throws is contained as the first's is (logged, the cast interrupted) and ends the
    /// cast, while the lanes already in flight fly on.
    /// </remarks>
    private void Fire(IUnit caster, IAbility ability, AbilityScript script, float cooldownSeconds, uint castId, CastView view,
        AbilityFootprint? dodge, AbilityFootprint? started, AbilityAim aim)
    {
        if (caster is not CharacterEntity { GodMode: true })
            ability.CooldownTimer = cooldownSeconds;
        arena.BroadcastFinishCast(caster, ability, castId);

        // The shape the cast was taken with: its own copy's, which a reshape of the caster's ability never reaches.
        IAbility shaped = CopyOf(script, ability);
        int lanes = LanesOf(script, ability);
        if (lanes > 1)
            started = null;

        // Contained (#530): a throwing Prepare never enters the active list, so it is never a world
        // object, and the cast is interrupted. What it spent stays spent. So is a script whose state cannot be
        // read once it has prepared.
        if (!TryPrepare(caster, ability, script, castId, view, dodge, started, out bool running))
            return;

        if (!running)
        {
            // Finished in Prepare, it never runs: a pooled script goes back now (#1181 Part D), any other is let go. Every
            // circle and cone ends here (#1238), its held hits keeping copies of their own. A fan whose first lane ended
            // there fires no other: its set of hit units went back with it.
            _pool.Retire(script);
            CombatLog.AbilityFired(_logger, ability.AbilityId, caster.Guid);
            return;
        }

        Run(caster, ability, script, castId, view);
        for (int lane = 1; lane < lanes; lane++)
        {
            if (Build(caster, aim, lane, shaped) is not { } next)
                continue;

            if (next is not IProjectileFan fan || script is not IProjectileFan first)
            {
                _pool.Return(next);
                continue;
            }

            fan.ShareHitsWith(first);
            if (!TryPrepare(caster, ability, next, castId, view, dodge, null, out bool flying))
                break;

            if (flying)
                Run(caster, ability, next, castId, view);
            else
                _pool.Retire(next);
        }

        // Fired, with the lanes already in flight, even when a later lane's Prepare threw and ended the cast.
        CombatLog.AbilityFired(_logger, ability.AbilityId, caster.Guid);
    }

    /// <summary>
    /// <paramref name="script" />'s <c>Prepare</c>, contained (#530), with the cast's id, start footprint, rewind and dodge
    /// footprint set on the arena and the values found before put back after. False, the failure logged and the cast
    /// interrupted, when it threw; else <paramref name="running" /> says whether it is still running.
    /// </summary>
    private bool TryPrepare(IUnit caster, IAbility ability, AbilityScript script, uint castId, CastView view,
        AbilityFootprint? dodge, AbilityFootprint? started, out bool running)
    {
        int outerRewind = arena.HitRewindTicks;
        IUnit? outerExempt = arena.HitRewindExempt;
        AbilityFootprint? outerDodge = arena.DodgeFootprint;
        AbilityFootprint? outerStarted = arena.FootprintInFlight;
        try
        {
            arena.CastInFlight = castId;
            arena.FootprintInFlight = started;
            arena.HitRewindTicks = view.HitRewindTicks;
            arena.HitRewindExempt = caster;
            arena.DodgeFootprint = dodge;
            script.Prepare();
            running = script.State is not SpellState.Finished;
            return true;
        }
        catch (Exception e)
        {
            // Interrupted after the finish on purpose: a circle or a cone deals its damage inside
            // Prepare, so the finish has to go out before it, and a throw can only be told after.
            Failed(caster, ability, e, "Prepare", castId);
            running = false;
            return false;
        }
        finally
        {
            arena.CastInFlight = 0;
            arena.FootprintInFlight = outerStarted;
            arena.HitRewindTicks = outerRewind;
            arena.HitRewindExempt = outerExempt;
            arena.DodgeFootprint = outerDodge;
        }
    }

    /// <summary>A prepared script still running joins the running list, in a reused entry.</summary>
    private void Run(IUnit caster, IAbility ability, AbilityScript script, uint castId, CastView view)
    {
        if (!_freeEntries.TryPop(out ActiveScript? entry))
            entry = new ActiveScript();
        entry.Start(caster, ability, script, castId, view);
        _activeAbilities.Add(entry);
    }

    /// <summary>How many projectiles a cast of <paramref name="meta" /> fires (#1181 Part D): its count for a projectile, else 1.</summary>
    private static int LanesOf(AbilityMetadata meta) =>
        meta.Shape == AbilityShape.Projectile ? Math.Max(1, meta.ProjectileCount) : 1;

    /// <summary>
    /// How many projectiles the cast <paramref name="script" /> was built for fires: its own copy's count when its type
    /// flies fans (<see cref="IProjectileFan" />), else 1, as <see cref="Build" /> turned its lane.
    /// </summary>
    private static int LanesOf(AbilityScript script, IAbility ability) =>
        script is IProjectileFan ? LanesOf(CopyOf(script, ability).Metadata) : 1;

    /// <summary>
    /// The ability as the cast was taken: a pooled script's own copy (<see cref="PooledAbilityScript.CastCopy" />),
    /// which a reshape of the caster's ability never reaches; else the caster's.
    /// </summary>
    private static IAbility CopyOf(AbilityScript script, IAbility ability) =>
        script is PooledAbilityScript pooled ? pooled.CastCopy : ability;

    /// <summary>
    /// The pool of this instance's scripts, keyed by type: projectiles (#1181 Part D), circles and cones (#1238). Each type
    /// is built by its factory here with the four cast arguments and no container service, so a pooled type that declared
    /// one would not compile here.
    /// </summary>
    private static AbilityScriptPool NewPool(IAbilityArena arena)
    {
        var pool = new AbilityScriptPool(arena);
        pool.Pools<ProjectileAbilityScript>(static (owner, own, caster, aim, at) =>
            new ProjectileAbilityScript(own, caster, aim, at) { Pool = owner });
        pool.Pools<CircleAbilityScript>(static (owner, own, caster, aim, at) =>
            new CircleAbilityScript(own, caster, aim, at) { Pool = owner });
        pool.Pools<ConeAbilityScript>(static (owner, own, caster, aim, at) =>
            new ConeAbilityScript(own, caster, aim, at) { Pool = owner });
        return pool;
    }

    /// <summary>
    /// A script that threw (#530): logged, and its caster sent the interrupt a failed cast gets, so no
    /// cast bar is left running. The caller drops the script.
    /// </summary>
    private void Failed(IUnit caster, IAbility ability, Exception e, string stage, uint castId)
    {
        _logger.LogError(e, "Ability script {Stage} threw and was dropped ability={AbilityId} caster={CasterId}",
            stage, ability.AbilityId, caster.Guid);
        TryInterrupt(caster, ability, castId);
    }

    /// <summary>Sends the interrupt, contained, so a failing send cannot take the rest of the tick with it.</summary>
    private void TryInterrupt(IUnit caster, IAbility ability, uint castId)
    {
        try
        {
            arena.BroadcastInterruptedCast(caster, ability, castId);
        }
        catch (Exception sendError)
        {
            _logger.LogError(sendError, "Sending the cast interrupt failed ability={AbilityId} caster={CasterId}",
                ability.AbilityId, caster.Guid);
        }
    }

    /// <summary>
    /// The next cast id (#648): counted per instance, so unique within it, skipping 0, which a packet from before
    /// #648 decodes as.
    /// </summary>
    public uint TakeCastId()
    {
        _lastCastId = _lastCastId == uint.MaxValue ? 1u : _lastCastId + 1u;
        return _lastCastId;
    }

    /// <summary>
    /// The footprint a queued cast will land on (#648), resolved as its script will resolve it: for a fan (#1181 Part
    /// D) resolved along the aim the lanes are placed about, naming the cast's <paramref name="lanes" />
    /// (<see cref="AbilityFootprint.Lanes" />), so every client can draw the whole fan. Contained: a footprint that
    /// cannot be resolved is sent as none, and the cast goes on.
    /// </summary>
    private AbilityFootprint? FootprintOf(IUnit caster, AbilityAim aim, IAbility ability, int lanes)
    {
        Vector3 origin = aim.Origin ?? caster.Position;
        try
        {
            AbilityFootprint? footprint =
                AbilityFootprint.Resolve(ability.Metadata, aim, origin, arena.GetNavigatorForPosition(origin));
            return footprint is { Shape: AbilityShape.Projectile } lane ? lane with { Lanes = (uint)lanes } : footprint;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Resolving the cast footprint failed ability={AbilityId} caster={CasterId}",
                ability.AbilityId, caster.Guid);
            return null;
        }
    }

    /// <summary>
    /// A running script and who cast it, with the caster's own ability and the cast's id for the interrupt, and how far
    /// the cast's hits are rewound (#1082), fixed when it was taken. Reused (#1181 Part D): started for each script that
    /// runs, cleared when it leaves the list.
    /// </summary>
    private sealed class ActiveScript
    {
        public IUnit Caster { get; private set; } = null!;
        public IAbility Ability { get; private set; } = null!;
        public AbilityScript Script { get; private set; } = null!;
        public uint CastId { get; private set; }
        public CastView View { get; private set; }

        /// <summary>Set by the sweep for finished scripts, for the removal that follows it in the same call.</summary>
        public bool Leaving { get; set; }

        /// <summary>Its script threw, or ran without a guid: never pooled again (#1181 Part D).</summary>
        public bool Threw { get; set; }

        public void Start(IUnit caster, IAbility ability, AbilityScript script, uint castId, CastView view)
        {
            Caster = caster;
            Ability = ability;
            Script = script;
            CastId = castId;
            View = view;
            Leaving = false;
            Threw = false;
        }

        /// <summary>No caster, ability or script kept alive by a free entry.</summary>
        public void Clear()
        {
            Caster = null!;
            Ability = null!;
            Script = null!;
        }
    }

    /// <summary>
    /// The running scripts in the order they fired, with an index by guid for <see cref="Find" /> (#883). The list is
    /// reachable only through this class, and every change to it empties the index; the first lookup after a change
    /// fills it again from the list, so the index never answers with a script the list has dropped.
    /// </summary>
    private sealed class ActiveScripts
    {
        private readonly List<ActiveScript> _scripts = [];
        private readonly Dictionary<ulong, AbilityScript> _byGuid = [];
        private bool _indexed;

        public List<ActiveScript>.Enumerator GetEnumerator() => _scripts.GetEnumerator();

        public void Add(ActiveScript script)
        {
            _scripts.Add(script);
            Changed();
        }

        public void Remove(ActiveScript script)
        {
            if (_scripts.Remove(script))
                Changed();
        }

        /// <summary>
        /// Removes every entry the sweep marked <see cref="ActiveScript.Leaving" />, keeping the others in order, and adds
        /// each removed one to <paramref name="removed" />, for the caller to hand back once the list is whole. A loop over
        /// a flag the sweep set, never a predicate: nothing it reads can throw, and none closes over the pool (#1181 Part D).
        /// </summary>
        public void RemoveLeaving(List<ActiveScript> removed)
        {
            int kept = 0;
            for (int i = 0; i < _scripts.Count; i++)
            {
                ActiveScript entry = _scripts[i];
                if (entry.Leaving)
                    removed.Add(entry);
                else
                    _scripts[kept++] = entry;
            }

            if (kept == _scripts.Count)
                return;

            _scripts.RemoveRange(kept, _scripts.Count - kept);
            Changed();
        }

        /// <summary>
        /// Removes every script <paramref name="caster" /> cast, adding each entry to <paramref name="removed" />, and
        /// answers how many. A loop: a predicate would close over the caster, a closure and a delegate per call (#883).
        /// </summary>
        public int RemoveCastBy(IUnit caster, List<ActiveScript> removed)
        {
            int count = 0;
            for (int i = _scripts.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_scripts[i].Caster, caster))
                {
                    removed.Add(_scripts[i]);
                    _scripts.RemoveAt(i);
                    count++;
                }
            }

            if (count > 0)
                Changed();
            return count;
        }

        /// <summary>
        /// The first script in the list with that guid, as a walk of the list answers. The index is trusted only for a
        /// script that still carries the guid it was indexed under: a script may set its guid again while it runs, and
        /// one the index cannot answer for is looked for in the list.
        /// </summary>
        public AbilityScript? Find(ObjectGuid guid)
        {
            if (_scripts.Count > WalkedUpTo)
            {
                if (!_indexed)
                    Index();

                if (_byGuid.TryGetValue(guid.RawValue, out AbilityScript? indexed) && indexed.Guid == guid)
                    return indexed;
            }

            for (int i = 0; i < _scripts.Count; i++)
            {
                AbilityScript script = _scripts[i].Script;
                if (script.Guid == guid)
                    return script;
            }

            return null;
        }

        /// <summary>
        /// Emptied now rather than at the next lookup, which may never come: until then the index would keep the
        /// scripts that left, and through them their casters, alive.
        /// </summary>
        private void Changed()
        {
            _byGuid.Clear();
            _indexed = false;
        }

        private void Index()
        {
            // Sized once for the list: filled from empty it would grow, and allocate, three times on its way to nine.
            _byGuid.EnsureCapacity(_scripts.Count);
            foreach (ActiveScript active in _scripts)
            {
                // The first of a guid wins, as in a walk. A script that left Prepare without a guid is not indexed.
                if (active.Script.Guid is { } guid)
                    _byGuid.TryAdd(guid.RawValue, active.Script);
            }

            _indexed = true;
        }
    }
}
