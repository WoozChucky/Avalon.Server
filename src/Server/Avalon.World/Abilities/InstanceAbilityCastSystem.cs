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
    IWorldObject? GetAbility(ObjectGuid guid);
}

public class InstanceAbilityCastSystem(
    ILoggerFactory factory,
    IServiceProvider serviceProvider,
    IScriptManager scriptManager,
    IAbilityArena arena)
    : IAbilityCastSystem
{
    private readonly List<AbilityScript> _activeAbilities = [];
    private readonly ILogger<InstanceAbilityCastSystem> _logger = factory.CreateLogger<InstanceAbilityCastSystem>();
    private readonly HashSet<ObjectGuid> _removeScheduled = [];
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
            Caster = character, Aim = aim, Ability = ability, Script = script, CastStartPosition = character.Position,
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
        // #521 item 3: nothing is removed from the queue while it is enumerated.
        _dequeued.Clear();

        foreach (AbilityInstance cast in _abilityQueue)
        {
            IAbility ability = cast.Ability;
            ability.CastTimeTimer -= (float)deltaTime.TotalSeconds;

            if (cast.CastStartPosition != cast.Caster.Position)
            {
                _logger.LogInformation("Cast interrupted by movement ability={AbilityId} caster={CharId}",
                    ability.AbilityId, cast.Caster.Guid);
                ResetCast(ability);
                cast.Caster.SendInterruptedCastAnimation(ability);
                _dequeued.Add(cast);
                continue;
            }

            if (ability.CastTimeTimer > 0)
            {
                continue;
            }

            _dequeued.Add(cast);
            ResetCast(ability);

            // A caster who died during the cast casts nothing, and is left free to cast again.
            if (cast.Caster is ICharacter { IsDead: true })
            {
                _logger.LogDebug("Dropped the cast of a dead caster ability={AbilityId} caster={CharId}",
                    ability.AbilityId, cast.Caster.Guid);
                continue;
            }

            Fire(cast.Caster, ability, cast.Script);
        }

        foreach (AbilityInstance cast in _dequeued)
        {
            _abilityQueue.Remove(cast);
        }

        foreach (AbilityScript script in _activeAbilities)
        {
            script.Update(deltaTime);

            if (script.State is SpellState.Finished)
            {
                _removeScheduled.Add(script.Guid);
            }

            if (script.Guid.Type == ObjectType.SpellProjectile)
            {
                objects.Add(script);
            }
        }

        foreach (ObjectGuid id in _removeScheduled)
        {
            _activeAbilities.RemoveAll(p => p.Guid == id);
        }

        _removeScheduled.Clear();
    }

    public IWorldObject? GetAbility(ObjectGuid guid) => _activeAbilities.Find(p => p.Guid == guid);

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
        caster.SendFinishCastAnimation(ability);
        script.Prepare();

        if (script.State is not SpellState.Finished)
        {
            _activeAbilities.Add(script);
        }

        _logger.LogDebug("Fired ability {AbilityId} by {CasterId}", ability.AbilityId, caster.Guid);
    }
}
