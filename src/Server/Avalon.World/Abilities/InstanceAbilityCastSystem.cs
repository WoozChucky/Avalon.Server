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
    bool QueueAbility(ICharacter character, IUnit? target, IAbility ability);
    void Update(TimeSpan deltaTime, List<IWorldObject> objects);
    IWorldObject? GetAbility(ObjectGuid guid);
}

public class InstanceAbilityCastSystem(ILoggerFactory factory, IServiceProvider serviceProvider, IScriptManager scriptManager)
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
                return false;

            if (character.CurrentPower < ability.Metadata.Cost)
                return false;

            character.CurrentPower -= ability.Metadata.Cost;
        }

        ability.Casting = true;
        AbilityInstance abilityInstance = new()
        {
            Caster = character, Target = target, Ability = ability, CastStartPosition = character.Position
        };
        return _abilityQueue.Add(abilityInstance);
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
                    abilityInstance.Target) is not AbilityScript abilityScript)
#pragma warning restore CS8604 // Possible null reference argument.
            {
                _logger.LogWarning("Failed to create ability script {ScriptName}",
                    abilityInstance.Ability.Metadata.ScriptName);
                continue;
            }

            abilityInstance.Caster.SendFinishCastAnimation(abilityInstance.Ability);

            abilityScript.Prepare();
            _activeAbilities.Add(abilityScript);

            _logger.LogDebug("Finished ability {AbilityId} cast by {CharacterId} on {CreatureId}",
                abilityInstance.Ability.AbilityId, abilityInstance.Caster.Guid, abilityInstance.Target?.Guid);
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
