using Avalon.Common;
using Avalon.World.Maps;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Avalon.World.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Spells;

public interface ISpellQueueSystem
{
    bool QueueSpell(ICharacter character, IUnit? target, ISpell spell);
    void Update(TimeSpan deltaTime, List<IWorldObject> objects);
    IWorldObject? GetSpell(ObjectGuid guid);
}

public class ChunkSpellSystem(ILoggerFactory factory, IServiceProvider serviceProvider, IScriptManager scriptManager)
    : ISpellQueueSystem
{
    private readonly ILogger<ChunkSpellSystem> _logger = factory.CreateLogger<ChunkSpellSystem>();
    
    private readonly HashSet<SpellInstance> _spellQueue = [];
    private readonly List<SpellScript> _activeSpells = [];
    private readonly HashSet<ObjectGuid> _removeScheduled = [];
    
    public bool QueueSpell(ICharacter character, IUnit? target, ISpell spell)
    {
        //TODO: Power cost deduction
        
        spell.Casting = true;
        var spellInstance = new SpellInstance
        {
            Caster = character,
            Target = target,
            SpellInfo = spell,
            CastStartPosition = character.Position
        };
        return _spellQueue.Add(spellInstance);
    }
    
    public void Update(TimeSpan deltaTime, List<IWorldObject> objects)
    {
        foreach (var spellInstance in _spellQueue)
        {
            spellInstance.SpellInfo.CastTimeTimer -= (float) deltaTime.TotalSeconds;
            
            // Check if the spell was interrupted by movement
            if (spellInstance.CastStartPosition != spellInstance.Caster.Position && spellInstance.SpellInfo.Metadata.CastTime > 0)
            {
                spellInstance.SpellInfo.CastTimeTimer = spellInstance.SpellInfo.Metadata.CastTime;
                spellInstance.SpellInfo.Casting = false;
                if (spellInstance.Caster is ICharacter character)
                {
                    character.SendInterruptedCastAnimation(spellInstance.SpellInfo);
                }
                _spellQueue.Remove(spellInstance);
                continue;
            }
            
            if (!(spellInstance.SpellInfo.CastTimeTimer <= 0)) continue;
            
            spellInstance.SpellInfo.Casting = false;
            spellInstance.SpellInfo.CooldownTimer = spellInstance.SpellInfo.Metadata.Cooldown;
            spellInstance.SpellInfo.CastTimeTimer = spellInstance.SpellInfo.Metadata.CastTime;
            _spellQueue.Remove(spellInstance);
            
            var scriptType = scriptManager.GetSpellScript(spellInstance.SpellInfo.Metadata.ScriptName);
            if (scriptType is null)
            {
                _logger.LogWarning("Spell script {ScriptName} not found", spellInstance.SpellInfo.Metadata.ScriptName);
                continue;
            }
            
            var info = spellInstance.SpellInfo.Clone();
            
#pragma warning disable CS8604 // Possible null reference argument.
            if (ActivatorUtilities.CreateInstance(serviceProvider, scriptType, info, spellInstance.Caster, spellInstance.Target) is not SpellScript spellScript)
#pragma warning restore CS8604 // Possible null reference argument.
            {
                _logger.LogWarning("Failed to create spell script {ScriptName}", spellInstance.SpellInfo.Metadata.ScriptName);
                continue;
            }
            
            spellInstance.Caster.SendFinishCastAnimation(spellInstance.SpellInfo);
            
            spellScript.Prepare();
            _activeSpells.Add(spellScript);
                
            _logger.LogDebug("Finished spell {SpellId} cast by {CharacterId} on {CreatureId}", spellInstance.SpellInfo.SpellId, spellInstance.Caster.Guid, spellInstance.Target?.Guid);
        }

        foreach (var spell in _activeSpells)
        {
            spell.Update(deltaTime);

            if (spell.State is SpellScript.SpellState.Finished)
            {
                _removeScheduled.Add(spell.Guid);
            }
            
            if (spell.Guid.Type == ObjectType.SpellProjectile)
            {
                objects.Add(spell);
            }
        }
        
        
        foreach (var id in _removeScheduled)
        {
            _activeSpells.RemoveAll(p => p.Guid == id);
        }
        _removeScheduled.Clear();
    }

    public IWorldObject? GetSpell(ObjectGuid guid)
    {
        return _activeSpells.Find(p => p.Guid == guid) ?? null;
    }
}