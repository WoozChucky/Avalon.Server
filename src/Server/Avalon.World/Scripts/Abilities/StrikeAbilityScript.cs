using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Abilities;

public class StrikeAbilityScript(ILogger<StrikeAbilityScript> logger, IAbility ability, IUnit caster, IUnit? target)
    : AbilityScript(ability,
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
            logger.LogWarning("Strike ability has no target");
            State = SpellState.Finished;
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (State is SpellState.Finished)
        {
            return;
        }

        if (Vector3.Distance(Position, Target!.Position) <= (uint)Ability.Metadata.Range + 1)
        {
            Caster.SendAttackAnimation(Ability);
            logger.LogInformation("Ability {AbilityId} hit {CreatureId}", Ability.AbilityId, Target.Guid);
            Target.OnHit(Caster, Ability.Metadata.EffectValue);
            State = SpellState.Finished;
        }
    }

    protected override bool ShouldRun() => true;

    public override AbilityScript Clone()
    {
        StrikeAbilityScript clone = new(logger, Ability, Caster, Target)
        {
            Guid = Guid, Position = Position, Velocity = Velocity, Orientation = Orientation
        };
        return clone;
    }
}
