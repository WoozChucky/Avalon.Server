using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Spells;

public class StrikeSpellScript : SpellScript
{
    private readonly ILogger<StrikeSpellScript> _logger;
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }
    
    public StrikeSpellScript(ILogger<StrikeSpellScript> logger, ISpell spell, IUnit caster, IUnit? target) : base(spell, caster, target)
    {
        _logger = logger;
    }
    
    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.Spell, IObject.GenerateId());
        Position = Caster.Position;
        if (Target == null)
        {
            _logger.LogWarning("Strike spell has no target");
            State = SpellState.Finished;
        }
    }
    
    public override void Update(TimeSpan deltaTime)
    {
        if (State is SpellState.Finished) return;
        
        if (Vector3.Distance(Position, Target!.Position) <= 1.5f)
        {
            Caster.SendAttackAnimation(Spell);
            _logger.LogInformation("Spell {SpellId} hit {CreatureId}", Spell.SpellId, Target.Guid);
            Target.OnHit(Caster, 0);
            State = SpellState.Finished;
        }
    }

    public override object State { get; set; } = SpellState.Executing;
    protected override bool ShouldRun()
    {
        return true;
    }

    public override SpellScript Clone()
    {
        var clone =  new StrikeSpellScript(_logger, Spell, Caster, Target)
        {
            Guid = Guid,
            Position = Position,
            Velocity = Velocity,
            Orientation = Orientation
        };
        return clone;
    }
}
