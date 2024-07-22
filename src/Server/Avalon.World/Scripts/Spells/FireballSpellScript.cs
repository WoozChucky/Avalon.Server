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
    public override ObjectGuid Guid { get; init; }

    private const float ProjectileSpeed = 5f;
    
    private static readonly Vector3 SpellHeightOffset = new(0, 0.5f, 0);
    
    private readonly ILogger<FireballSpellScript> _logger;
    
    public FireballSpellScript(ILoggerFactory loggerFactory, ISpell spell, IUnit caster, IUnit? target) : base(spell, caster, target)
    {
        Guid = new ObjectGuid(ObjectType.SpellProjectile, IObject.GenerateId());
        _logger = loggerFactory.CreateLogger<FireballSpellScript>();
        Position = caster.Position + SpellHeightOffset;
        Velocity = Vector3.Normalize(target?.Position ?? caster.Position);
    }

    public override object State { get; set; } = true;
    protected override bool ShouldRun()
    {
        return State is true;
    }
    
    public override void Update(TimeSpan deltaTime)
    {
        if (State is false) return;
        
        if (Vector3.Distance(Position, Target!.Position + SpellHeightOffset) < 0.1f)
        {
            _logger.LogInformation("Spell {SpellId} hit {CreatureId}", Spell.SpellId, Target.Guid);
            Target.OnHit(Caster, 0);
            State = false;
        }
        
        Velocity = Vector3.Normalize(Target.Position + SpellHeightOffset - Position);
        Position += Velocity * ProjectileSpeed * (float) deltaTime.TotalSeconds;
    }
}
