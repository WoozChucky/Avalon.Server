using Avalon.Common;
using Avalon.Common.Mathematics;
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
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Avalon.Combat;

namespace Avalon.World.Abilities;

public interface IAbilityCastSystem
{
    /// <summary>
    /// Takes a cast-time cast aimed at <paramref name="aim" />, pays its cost, marks it casting and broadcasts its
    /// start, with its cast id and the footprint it will land on (#648). False, with nothing spent, nothing sent
    /// and <c>Casting</c> left clear, when its script is missing or cannot be built, or the cost cannot be paid.
    /// The script is built now, with this aim and the caster's position now as its origin, and fired once the
    /// cast time has run out. Any unit may cast (#163); a creature's casts cost nothing.
    /// </summary>
    bool QueueAbility(IUnit caster, AbilityAim aim, IAbility ability);

    /// <summary>
    /// Instant-cast counterpart to <see cref="QueueAbility" />: builds the script, pays the cost, starts
    /// the cooldown, broadcasts the finish-cast, runs <c>Prepare()</c>, and keeps the script ticking if it
    /// has not finished (a projectile). False, with nothing spent, when the script cannot be built or
    /// the cost cannot be paid.
    /// </summary>
    bool RunInstant(IUnit caster, AbilityAim aim, IAbility ability);

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
    /// clear the cast, and the caster could cast nothing anywhere else.
    /// </summary>
    void CancelCasts(IUnit caster);

    /// <summary>
    /// Drops every active script <paramref name="caster" /> cast, a projectile in flight included, so
    /// none of them hits, threatens or credits anything again (#541). A dropped projectile is no longer
    /// a world object, so every client still watching it is sent its removal on the next tick, as for
    /// one that finished. For a caster leaving the instance, called with <see cref="CancelCasts" />.
    /// </summary>
    void CancelScriptsOf(IUnit caster);

    IWorldObject? GetAbility(ObjectGuid guid);
}

public class InstanceAbilityCastSystem(
    ILoggerFactory factory,
    IServiceProvider serviceProvider,
    IScriptManager scriptManager,
    IAbilityArena arena)
    : IAbilityCastSystem
{
    private readonly List<ActiveScript> _activeAbilities = [];
    private readonly List<ActiveScript> _failed = [];
    private readonly List<(IUnit Caster, IAbility Ability, uint CastId)> _interrupts = [];
    private readonly ILogger<InstanceAbilityCastSystem> _logger = factory.CreateLogger<InstanceAbilityCastSystem>();
    private readonly HashSet<AbilityInstance> _abilityQueue = [];
    private readonly List<AbilityInstance> _dequeued = [];
    private uint _lastCastId;

    public bool QueueAbility(IUnit caster, AbilityAim aim, IAbility ability)
    {
        if (!IsFree(caster) && AbilityCost.Check(caster, ability.Metadata) is var cost and not CostCheck.Payable)
        {
            _logger.LogInformation("QueueAbility reject {Cost} ability={AbilityId} powerType={PowerType}",
                cost, ability.AbilityId, caster.PowerType);
            return false;
        }

        // #648: the cast resolves from where the caster stands now, not where it stands when it fires, so it
        // lands on the footprint its start broadcast carries.
        aim = aim with { Origin = caster.Position };

        // Built now, with the aim captured at cast start, so a script that is missing or cannot be
        // built is refused before anything is paid, and a reload that removes it mid-cast costs nothing.
        if (Build(caster, aim, ability) is not { } script)
        {
            return false;
        }

        // #627: haste is read once, here: the cast time is fixed now, and the cooldown it sets when it fires
        // uses the same value, so a gear change mid-cast changes neither.
        float haste = HasteOf(caster);
        uint castId = NextCastId();
        _abilityQueue.Add(new AbilityInstance
        {
            Caster = caster, Ability = ability, Script = script, CastStartPosition = caster.Position,
            HastePct = haste, CastId = castId,
        });

        // #521 item 1: Casting is set, and the cost paid, only once the queue has taken the cast.
        ability.Casting = true;
        ability.CastTimeTimer = Haste.Scale(ability.Metadata.CastTime, haste);
        if (!IsFree(caster))
        {
            AbilityCost.Pay(caster, ability.Metadata);
        }

        _logger.LogDebug("QueueAbility queued ability={AbilityId} caster={CharId} castTime={CastTime}s queueSize={Size}",
            ability.AbilityId, caster.Guid, ability.Metadata.CastTime, _abilityQueue.Count);

        arena.BroadcastUnitStartCast(caster, ability, castId, FootprintOf(caster, aim, ability));
        return true;
    }

    public bool RunInstant(IUnit caster, AbilityAim aim, IAbility ability)
    {
        if (!IsFree(caster) && AbilityCost.Check(caster, ability.Metadata) is var cost and not CostCheck.Payable)
        {
            _logger.LogInformation("RunInstant reject {Cost} ability={AbilityId} powerType={PowerType}",
                cost, ability.AbilityId, caster.PowerType);
            return false;
        }

        aim = aim with { Origin = caster.Position };
        if (Build(caster, aim, ability) is not { } script)
        {
            return false;
        }

        if (!IsFree(caster))
        {
            AbilityCost.Pay(caster, ability.Metadata);
        }

        Fire(caster, ability, script, HasteOf(caster), NextCastId());
        return true;
    }

    public void Update(TimeSpan deltaTime, List<IWorldObject> objects)
    {
        // A script that finished on an earlier tick leaves now, not on the tick it finished (#164): a
        // projectile stays a world object until its final state (where it stopped, zero velocity) has
        // been taken for a broadcast, so every client sees it spawn, stop and despawn, even one that
        // ended on the tick it was first seen. Updates go out only on broadcast ticks, so that can be
        // a few ticks later.
        _activeAbilities.RemoveAll(static a => a.Script.State is SpellState.Finished
            && (a.Script.Guid.Type != ObjectType.SpellProjectile || !a.Script.HasUnsentChanges));

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

        foreach (AbilityInstance cast in _abilityQueue)
        {
            IAbility ability = cast.Ability;
            ability.CastTimeTimer -= (float)deltaTime.TotalSeconds;

            // #163: a creature that died during its wind-up, or turned for home (the leash, a lost target),
            // ends the cast at once and out loud, however much of it was left, so its corpse or its walk home
            // never lands the hit.
            if (IsAbandonedByCreature(cast.Caster))
            {
                _logger.LogDebug("Dropped the cast of a creature that died or turned for home ability={AbilityId} caster={CasterId}",
                    ability.AbilityId, cast.Caster.Guid);
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
                _logger.LogInformation("Cast interrupted by movement ability={AbilityId} caster={CharId}",
                    ability.AbilityId, cast.Caster.Guid);
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
                _logger.LogDebug("Dropped the cast of a dead caster ability={AbilityId} caster={CharId}",
                    ability.AbilityId, cast.Caster.Guid);
                _interrupts.Add((cast.Caster, ability, cast.CastId));
                continue;
            }

            Fire(cast.Caster, ability, cast.Script, cast.HastePct, cast.CastId);
        }

        foreach (AbilityInstance cast in _dequeued)
        {
            _abilityQueue.Remove(cast);
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
    /// is contained (#530): a script that throws is dropped, and the others still tick.
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

            // A finished script is never ticked again, so nothing it does is applied twice.
            if (script.State is not SpellState.Finished)
            {
                try
                {
                    script.Update(deltaTime);
                }
                catch (Exception e)
                {
                    _failed.Add(active);
                    Failed(active.Caster, active.Ability, e, "Update", active.CastId);
                    continue;
                }
            }

            if (script.Guid.Type == ObjectType.SpellProjectile)
            {
                objects.Add(script);
            }
        }

        // Left out of this tick's world objects, a dropped projectile is removed from every client's
        // view like one that finished.
        foreach (ActiveScript failed in _failed)
        {
            _activeAbilities.Remove(failed);
        }
    }

    public void CancelCasts(IUnit caster)
    {
        // Collected first: nothing is removed from the queue while it is enumerated.
        List<AbilityInstance> cancelled = [];
        foreach (AbilityInstance cast in _abilityQueue)
        {
            if (ReferenceEquals(cast.Caster, caster))
            {
                cancelled.Add(cast);
            }
        }

        foreach (AbilityInstance cast in cancelled)
        {
            _abilityQueue.Remove(cast);
            ResetCast(cast.Ability);
        }

        // Sent once every cast is already cleared, so a failing send cannot leave one stuck.
        foreach (AbilityInstance cast in cancelled)
        {
            _logger.LogInformation("Cast cancelled as its caster left ability={AbilityId} caster={CharId}",
                cast.Ability.AbilityId, caster.Guid);
            arena.BroadcastInterruptedCast(caster, cast.Ability, cast.CastId);
        }
    }

    public void CancelScriptsOf(IUnit caster)
    {
        int dropped = _activeAbilities.RemoveAll(a => ReferenceEquals(a.Caster, caster));
        if (dropped > 0)
        {
            _logger.LogInformation("Dropped {Count} active scripts as their caster left caster={CharId}",
                dropped, caster.Guid);
        }
    }

    public void DropFinished() => _activeAbilities.RemoveAll(static a => a.Script.State is SpellState.Finished);

    public IWorldObject? GetAbility(ObjectGuid guid) => _activeAbilities.Find(a => a.Script.Guid == guid)?.Script;

    private static void ResetCast(IAbility ability)
    {
        ability.Casting = false;
        ability.CastTimeTimer = ability.Metadata.CastTime;
    }

    private AbilityScript? Build(IUnit caster, AbilityAim aim, IAbility ability)
    {
        Type? scriptType = scriptManager.GetAbilityScript(ability.Metadata.ScriptName);
        if (scriptType is null)
        {
            _logger.LogWarning("Ability script {ScriptName} not found", ability.Metadata.ScriptName);
            return null;
        }

        try
        {
            return ActivatorUtilities.CreateInstance(serviceProvider, scriptType, ability.Clone(), caster, aim, arena)
                as AbilityScript;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to build ability script {ScriptName}", ability.Metadata.ScriptName);
            return null;
        }
    }

    /// <summary>
    /// A caster's effective haste (#627): a character's, cached at its last stats refresh; a World-side
    /// creature's, capped by the cap it spawned with (#163). Any other caster casts with none.
    /// </summary>
    private static float HasteOf(IUnit caster) => caster switch
    {
        CharacterEntity character => character.EffectiveHastePct,
        Creature creature => MathF.Min(creature.HastePct, creature.HasteCap),
        _ => 0f,
    };

    /// <summary>Creatures and staff characters in god mode do not pay ability costs.</summary>
    private static bool IsFree(IUnit caster) => caster is ICreature or CharacterEntity { GodMode: true };

    /// <summary>A character is dead by its flag, a creature at 0 health.</summary>
    private static bool IsDead(IUnit caster) => caster switch
    {
        ICharacter character => character.IsDead,
        ICreature creature => creature.CurrentHealth == 0,
        _ => false,
    };

    /// <summary>A creature caster that died, or that its combat script is walking home (#163, #610).</summary>
    private static bool IsAbandonedByCreature(IUnit caster) =>
        caster is ICreature creature
        && (creature.CurrentHealth == 0 || creature.Script is IReturningHome { IsReturningHome: true });

    /// <summary>
    /// The cooldown a cast sets when it fires. A creature's basic waits its SwingInterval (#163), which already
    /// carries its haste, so it is not divided again; every other cooldown is divided by the haste the cast was
    /// read with (#627).
    /// </summary>
    private static float CooldownOf(IUnit caster, IAbility ability, float hastePct) =>
        caster is Creature creature && creature.Abilities.IsBasic(ability)
            ? creature.SwingInterval
            : Haste.Scale(ability.Metadata.Cooldown, hastePct);

    /// <summary>
    /// The cooldown, set once (#627), so a later gear change never rescales a running one; the finish-cast
    /// broadcast; the effect. A script still running keeps ticking.
    /// </summary>
    private void Fire(IUnit caster, IAbility ability, AbilityScript script, float hastePct, uint castId)
    {
        if (caster is not CharacterEntity { GodMode: true })
            ability.CooldownTimer = CooldownOf(caster, ability, hastePct);
        arena.BroadcastFinishCast(caster, ability, castId);

        // Contained (#530): a throwing Prepare never enters the active list, so it is never a world
        // object, and the cast is interrupted. What it spent stays spent.
        try
        {
            arena.CastInFlight = castId;
            script.Prepare();
        }
        catch (Exception e)
        {
            // Interrupted after the finish on purpose: a circle or a cone deals its damage inside
            // Prepare, so the finish has to go out before it, and a throw can only be told after.
            Failed(caster, ability, e, "Prepare", castId);
            return;
        }
        finally
        {
            arena.CastInFlight = 0;
        }

        if (script.State is not SpellState.Finished)
        {
            _activeAbilities.Add(new ActiveScript(caster, ability, script, castId));
        }

        _logger.LogDebug("Fired ability {AbilityId} by {CasterId}", ability.AbilityId, caster.Guid);
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
    private uint NextCastId()
    {
        _lastCastId = _lastCastId == uint.MaxValue ? 1u : _lastCastId + 1u;
        return _lastCastId;
    }

    /// <summary>
    /// The footprint a queued cast will land on (#648), resolved as its script will resolve it. Contained: a
    /// footprint that cannot be resolved is sent as none, and the cast goes on.
    /// </summary>
    private AbilityFootprint? FootprintOf(IUnit caster, AbilityAim aim, IAbility ability)
    {
        Vector3 origin = aim.Origin ?? caster.Position;
        try
        {
            return AbilityFootprint.Resolve(ability.Metadata, aim, origin, arena.GetNavigatorForPosition(origin));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Resolving the cast footprint failed ability={AbilityId} caster={CasterId}",
                ability.AbilityId, caster.Guid);
            return null;
        }
    }

    /// <summary>A running script and who cast it, with the caster's own ability and the cast's id for the interrupt.</summary>
    private sealed class ActiveScript(IUnit caster, IAbility ability, AbilityScript script, uint castId)
    {
        public IUnit Caster { get; } = caster;
        public IAbility Ability { get; } = ability;
        public AbilityScript Script { get; } = script;
        public uint CastId { get; } = castId;
    }
}
