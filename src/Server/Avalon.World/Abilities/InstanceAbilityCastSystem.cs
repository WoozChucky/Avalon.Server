using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Abilities;

public interface IAbilityCastSystem
{
    bool QueueAbility(ICharacter character, IUnit? target, IAbility ability);

    /// <summary>
    /// Instant-cast counterpart to <see cref="QueueAbility"/>: builds the script, fires the
    /// finish-cast animation, runs <c>Prepare()</c>, starts the cooldown, and registers the
    /// script with the active list so its post-cast effects (e.g. projectile travel,
    /// timed-damage scripts) tick in subsequent updates. Mirrors the cast-completion branch
    /// of <see cref="Update"/> exactly so queued and instant abilities resolve identically.
    /// </summary>
    void RunInstant(IUnit caster, IUnit? target, IAbility ability);

    void Update(TimeSpan deltaTime, List<IWorldObject> objects);
    IWorldObject? GetAbility(ObjectGuid guid);
}

public class InstanceAbilityCastSystem(
    ILoggerFactory factory,
    IServiceProvider serviceProvider,
    IScriptManager scriptManager,
    ISimulationContext simulationContext)
    : IAbilityCastSystem
{
    private readonly List<AbilityScript> _activeAbilities = [];
    private readonly ILogger<InstanceAbilityCastSystem> _logger = factory.CreateLogger<InstanceAbilityCastSystem>();
    private readonly HashSet<ObjectGuid> _removeScheduled = [];

    private readonly HashSet<AbilityInstance> _abilityQueue = [];

    public bool QueueAbility(ICharacter character, IUnit? target, IAbility ability)
    {
        // Power cost deduction: only Mana and Energy are depletion-based resources.
        // Fury and None are blocked until their mechanics are designed.
        if (ability.Metadata.Cost > 0)
        {
            if (character.PowerType is not (PowerType.Mana or PowerType.Energy))
            {
                _logger.LogInformation("QueueAbility reject PowerTypeMismatch ability={AbilityId} powerType={PowerType}",
                    ability.AbilityId, character.PowerType);
                return false;
            }

            if (character.CurrentPower < ability.Metadata.Cost)
            {
                _logger.LogInformation("QueueAbility reject Cost ability={AbilityId} need={Cost} have={Have}",
                    ability.AbilityId, ability.Metadata.Cost, character.CurrentPower);
                return false;
            }

            character.CurrentPower -= ability.Metadata.Cost;
        }

        ability.Casting = true;
        AbilityInstance abilityInstance = new()
        {
            Caster = character, Target = target, Ability = ability, CastStartPosition = character.Position
        };
        bool added = _abilityQueue.Add(abilityInstance);
        _logger.LogDebug("QueueAbility queued ability={AbilityId} caster={CharId} target={TargetGuid} castTimeMs={CastTime} added={Added} queueSize={Size}",
            ability.AbilityId, character.Guid, target?.Guid, ability.Metadata.CastTime, added, _abilityQueue.Count);
        return added;
    }

    public void RunInstant(IUnit caster, IUnit? target, IAbility ability)
    {
        // Mirror the completion branch of Update: cooldown is started, script is created,
        // finish-cast is broadcast, and the script is added to the active list so any
        // post-cast effects continue to tick.
        ability.Casting       = false;
        ability.CooldownTimer = ability.Metadata.Cooldown;
        ability.CastTimeTimer = ability.Metadata.CastTime;

        Type? scriptType = scriptManager.GetAbilityScript(ability.Metadata.ScriptName);
        if (scriptType is null)
        {
            _logger.LogWarning("Ability script {ScriptName} not found", ability.Metadata.ScriptName);
            return;
        }

        IAbility info = ability.Clone();

#pragma warning disable CS8604 // Possible null reference argument.
        if (ActivatorUtilities.CreateInstance(serviceProvider, scriptType, info, caster, target, simulationContext)
            is not AbilityScript abilityScript)
#pragma warning restore CS8604
        {
            _logger.LogWarning("Failed to create ability script {ScriptName}", ability.Metadata.ScriptName);
            return;
        }

        caster.SendFinishCastAnimation(ability);
        abilityScript.Prepare();
        _activeAbilities.Add(abilityScript);

        _logger.LogDebug("Finished instant ability {AbilityId} cast by {CasterId} on {TargetId}",
            ability.AbilityId, caster.Guid, target?.Guid);
    }

    public void Update(TimeSpan deltaTime, List<IWorldObject> objects)
    {
        foreach (AbilityInstance abilityInstance in _abilityQueue)
        {
            abilityInstance.Ability.CastTimeTimer -= (float)deltaTime.TotalSeconds;

            // Check if the ability was interrupted by movement
            if (abilityInstance.CastStartPosition != abilityInstance.Caster.Position &&
                abilityInstance.Ability.Metadata.CastTime > 0)
            {
                _logger.LogInformation("QueueAbility interrupt-by-movement ability={AbilityId} caster={CharId}",
                    abilityInstance.Ability.AbilityId, abilityInstance.Caster.Guid);
                abilityInstance.Ability.CastTimeTimer = abilityInstance.Ability.Metadata.CastTime;
                abilityInstance.Ability.Casting = false;
                if (abilityInstance.Caster is ICharacter character)
                {
                    character.SendInterruptedCastAnimation(abilityInstance.Ability);
                }

                _abilityQueue.Remove(abilityInstance);
                continue;
            }

            if (!(abilityInstance.Ability.CastTimeTimer <= 0))
            {
                continue;
            }

            abilityInstance.Ability.Casting = false;
            abilityInstance.Ability.CooldownTimer = abilityInstance.Ability.Metadata.Cooldown;
            abilityInstance.Ability.CastTimeTimer = abilityInstance.Ability.Metadata.CastTime;
            _abilityQueue.Remove(abilityInstance);

            Type? scriptType = scriptManager.GetAbilityScript(abilityInstance.Ability.Metadata.ScriptName);
            if (scriptType is null)
            {
                _logger.LogWarning("Ability script {ScriptName} not found", abilityInstance.Ability.Metadata.ScriptName);
                continue;
            }

            IAbility info = abilityInstance.Ability.Clone();

#pragma warning disable CS8604 // Possible null reference argument.
            if (ActivatorUtilities.CreateInstance(serviceProvider, scriptType, info, abilityInstance.Caster,
                    abilityInstance.Target, simulationContext) is not AbilityScript abilityScript)
#pragma warning restore CS8604 // Possible null reference argument.
            {
                _logger.LogWarning("Failed to create ability script {ScriptName}",
                    abilityInstance.Ability.Metadata.ScriptName);
                continue;
            }

            abilityInstance.Caster.SendFinishCastAnimation(abilityInstance.Ability);

            abilityScript.Prepare();
            _activeAbilities.Add(abilityScript);

            _logger.LogDebug("QueueAbility cast-finished ability={AbilityId} caster={CharId} target={TargetGuid} activeCount={Active}",
                abilityInstance.Ability.AbilityId, abilityInstance.Caster.Guid, abilityInstance.Target?.Guid, _activeAbilities.Count);
        }

        foreach (AbilityScript ability in _activeAbilities)
        {
            ability.Update(deltaTime);

            if (ability.State is SpellState.Finished)
            {
                _removeScheduled.Add(ability.Guid);
            }

            if (ability.Guid.Type == ObjectType.SpellProjectile)
            {
                objects.Add(ability);
            }
        }


        foreach (ObjectGuid id in _removeScheduled)
        {
            _activeAbilities.RemoveAll(p => p.Guid == id);
        }

        _removeScheduled.Clear();
    }

    public IWorldObject? GetAbility(ObjectGuid guid) => _activeAbilities.Find(p => p.Guid == guid);
}
