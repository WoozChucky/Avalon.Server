using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Spells;

public class FireballSpellScript : SpellScript
{
    private const float ProjectileSpeed = 5f;
    
    private static readonly Vector3 SpellHeightOffset = new(0, 0.5f, 0);
    
    private readonly ILogger<FireballSpellScript> _logger;
    
    private Vector3 _position;
    private Vector3 _velocity;
    
    
    
    public FireballSpellScript(ILoggerFactory loggerFactory, ISpell spell, IUnit caster, IUnit? target) : base(spell, caster, target)
    {
        _logger = loggerFactory.CreateLogger<FireballSpellScript>();
        _position = caster.Position + SpellHeightOffset;
        _velocity = Vector3.Normalize(target?.Position ?? caster.Position);
        // Id = IObject.GenerateId(),
    }

    public override object State { get; set; } = true;
    protected override bool ShouldRun()
    {
        return State is true;
    }
    
    public override void Update(TimeSpan deltaTime)
    {
        if (State is false) return;
        
        if (Vector3.Distance(_position, Target!.Position + SpellHeightOffset) < 0.1f)
        {
            _logger.LogInformation("Spell {SpellId} hit {CreatureId}", Spell.SpellId, Target.Guid);
            Target.OnHit(Caster, 0);
            State = false;
        }
        
        _velocity = Vector3.Normalize(Target.Position + SpellHeightOffset - _position);
        _position += _velocity * ProjectileSpeed * (float) deltaTime.TotalSeconds;
    }
}
