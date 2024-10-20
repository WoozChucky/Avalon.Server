using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Spells;

public class StrikeSpellScript(ILogger<StrikeSpellScript> logger, ISpell spell, IUnit caster, IUnit? target)
    : SpellScript(spell,
        caster, target)
{
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }

    public override object State { get; set; } = SpellState.Executing;

    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.Spell, IObject.GenerateId());
        Position = Caster.Position;
        if (Target == null)
        {
            logger.LogWarning("Strike spell has no target");
            State = SpellState.Finished;
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (State is SpellState.Finished)
        {
            return;
        }

        if (Vector3.Distance(Position, Target!.Position) <= (uint)Spell.Metadata.Range + 1)
        {
            Caster.SendAttackAnimation(Spell);
            logger.LogInformation("Spell {SpellId} hit {CreatureId}", Spell.SpellId, Target.Guid);
            Target.OnHit(Caster, Spell.Metadata.EffectValue);
            State = SpellState.Finished;
        }
    }

    protected override bool ShouldRun() => true;

    public override SpellScript Clone()
    {
        StrikeSpellScript clone = new(logger, Spell, Caster, Target)
        {
            Guid = Guid, Position = Position, Velocity = Velocity, Orientation = Orientation
        };
        return clone;
    }
}
