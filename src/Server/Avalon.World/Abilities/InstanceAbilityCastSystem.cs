using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Abilities;

public interface IAbilityCastSystem
{
    /// <summary>
    /// Takes a cast-time cast aimed at <paramref name="aim" />, pays its cost and marks it casting. False,
    /// with nothing spent and <c>Casting</c> left clear, when its script is missing or cannot be built, or
    /// the cost cannot be paid. The script is built now, with this aim, and fired once the cast time has run out.
    /// </summary>
    bool QueueAbility(ICharacter character, AbilityAim aim, IAbility ability);

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
    private readonly List<(IUnit Caster, IAbility Ability)> _interrupts = [];
    private readonly ILogger<InstanceAbilityCastSystem> _logger = factory.CreateLogger<InstanceAbilityCastSystem>();
    private readonly HashSet<AbilityInstance> _abilityQueue = [];
    private readonly List<AbilityInstance> _dequeued = [];

    public bool QueueAbility(ICharacter character, AbilityAim aim, IAbility ability)
    {
        CostCheck cost = AbilityCost.Check(character, ability.Metadata);
        if (cost != CostCheck.Payable)
        {
            _logger.LogInformation("QueueAbility reject {Cost} ability={AbilityId} powerType={PowerType}",
                cost, ability.AbilityId, character.PowerType);
            return false;
        }

        // Built now, with the aim captured at cast start, so a script that is missing or cannot be
        // built is refused before anything is paid, and a reload that removes it mid-cast costs nothing.
        if (Build(character, aim, ability) is not { } script)
        {
            return false;
        }

        _abilityQueue.Add(new AbilityInstance
        {
            Caster = character, Ability = ability, Script = script, CastStartPosition = character.Position,
        });

        // #521 item 1: Casting is set, and the cost paid, only once the queue has taken the cast.
        ability.Casting = true;
        AbilityCost.Pay(character, ability.Metadata);

        _logger.LogDebug("QueueAbility queued ability={AbilityId} caster={CharId} castTime={CastTime}s queueSize={Size}",
            ability.AbilityId, character.Guid, ability.Metadata.CastTime, _abilityQueue.Count);
        return true;
    }

    public bool RunInstant(IUnit caster, AbilityAim aim, IAbility ability)
    {
        CostCheck cost = AbilityCost.Check(caster, ability.Metadata);
        if (cost != CostCheck.Payable)
        {
            _logger.LogInformation("RunInstant reject {Cost} ability={AbilityId} powerType={PowerType}",
                cost, ability.AbilityId, caster.PowerType);
            return false;
        }

        if (Build(caster, aim, ability) is not { } script)
        {
            return false;
        }

        AbilityCost.Pay(caster, ability.Metadata);
        Fire(caster, ability, script);
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
    /// Runs down every queued cast's timer: a caster who moved is interrupted, a finished cast is
    /// fired, or dropped when its caster died. Every cast that ends leaves the queue after the loop,
    /// and its interrupt, if any, is collected for <see cref="SendInterrupts" />.
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

            if (cast.CastStartPosition != cast.Caster.Position)
            {
                _logger.LogInformation("Cast interrupted by movement ability={AbilityId} caster={CharId}",
                    ability.AbilityId, cast.Caster.Guid);
                ResetCast(ability);
                _interrupts.Add((cast.Caster, ability));
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
            if (cast.Caster is ICharacter { IsDead: true })
            {
                _logger.LogDebug("Dropped the cast of a dead caster ability={AbilityId} caster={CharId}",
                    ability.AbilityId, cast.Caster.Guid);
                _interrupts.Add((cast.Caster, ability));
                continue;
            }

            Fire(cast.Caster, ability, cast.Script);
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
        foreach ((IUnit caster, IAbility ability) in _interrupts)
        {
            TryInterrupt(caster, ability);
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
                    Failed(active.Caster, active.Ability, e, "Update");
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
            arena.BroadcastInterruptedCast(caster, cast.Ability);
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

    /// <summary>The cooldown, the finish-cast broadcast, the effect; a script still running keeps ticking.</summary>
    private void Fire(IUnit caster, IAbility ability, AbilityScript script)
    {
        ability.CooldownTimer = ability.Metadata.Cooldown;
        arena.BroadcastFinishCast(caster, ability);

        // Contained (#530): a throwing Prepare never enters the active list, so it is never a world
        // object, and the cast is interrupted. What it spent stays spent.
        try
        {
            script.Prepare();
        }
        catch (Exception e)
        {
            // Interrupted after the finish on purpose: a circle or a cone deals its damage inside
            // Prepare, so the finish has to go out before it, and a throw can only be told after.
            Failed(caster, ability, e, "Prepare");
            return;
        }

        if (script.State is not SpellState.Finished)
        {
            _activeAbilities.Add(new ActiveScript(caster, ability, script));
        }

        _logger.LogDebug("Fired ability {AbilityId} by {CasterId}", ability.AbilityId, caster.Guid);
    }

    /// <summary>
    /// A script that threw (#530): logged, and its caster sent the interrupt a failed cast gets, so no
    /// cast bar is left running. The caller drops the script.
    /// </summary>
    private void Failed(IUnit caster, IAbility ability, Exception e, string stage)
    {
        _logger.LogError(e, "Ability script {Stage} threw and was dropped ability={AbilityId} caster={CasterId}",
            stage, ability.AbilityId, caster.Guid);
        TryInterrupt(caster, ability);
    }

    /// <summary>Sends the interrupt, contained, so a failing send cannot take the rest of the tick with it.</summary>
    private void TryInterrupt(IUnit caster, IAbility ability)
    {
        try
        {
            arena.BroadcastInterruptedCast(caster, ability);
        }
        catch (Exception sendError)
        {
            _logger.LogError(sendError, "Sending the cast interrupt failed ability={AbilityId} caster={CasterId}",
                ability.AbilityId, caster.Guid);
        }
    }

    /// <summary>A running script and who cast it, with the caster's own ability for the interrupt.</summary>
    private sealed class ActiveScript(IUnit caster, IAbility ability, AbilityScript script)
    {
        public IUnit Caster { get; } = caster;
        public IAbility Ability { get; } = ability;
        public AbilityScript Script { get; } = script;
    }
}
