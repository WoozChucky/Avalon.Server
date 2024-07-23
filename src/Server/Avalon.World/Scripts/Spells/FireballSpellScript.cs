using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Spells;

public class FireballSpellScript : SpellScript
{
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }

    private const float ProjectileSpeed = 5f;
    
    private static readonly Vector3 SpellHeightOffset = new(0, 0.5f, 0);
    
    private readonly ILogger<FireballSpellScript> _logger;
    
    public FireballSpellScript(ILogger<FireballSpellScript> logger, ISpell spell, IUnit caster, IUnit? target) : base(spell, caster, target)
    {
        _logger = logger;
    }

    public override object State { get; set; } = SpellState.Executing;
    protected override bool ShouldRun()
    {
        return true;
    }

    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.SpellProjectile, IObject.GenerateId());
        Position = Caster.Position + SpellHeightOffset;
        if (Target == null)
        {
            _logger.LogWarning("Fireball spell has no target");
            State = SpellState.Finished;
        }
        Velocity = Vector3.Normalize(Target!.Position + SpellHeightOffset - Position);
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (State is SpellState.Finished) return;
        
        if (Vector3.Distance(Position, Target!.Position + SpellHeightOffset) < 0.1f)
        {
            _logger.LogInformation("Spell {SpellId} hit {CreatureId}", Spell.SpellId, Target.Guid);
            Target.OnHit(Caster, 0);
            State = SpellState.Finished;
            return;
        }
        
        Velocity = Vector3.Normalize(Target.Position + SpellHeightOffset - Position);
        Position += Velocity * ProjectileSpeed * (float) deltaTime.TotalSeconds;
    }
    
    public override SpellScript Clone()
    {
        var clone =  new FireballSpellScript(_logger, Spell, Caster, Target)
        {
            Guid = Guid,
            Position = Position,
            Velocity = Velocity,
            Orientation = Orientation
        };

        return clone;
    }
}
