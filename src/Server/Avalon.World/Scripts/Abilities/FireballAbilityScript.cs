using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Abilities;

public class FireballAbilityScript(ILogger<FireballAbilityScript> logger, IAbility ability, IUnit caster, IUnit? target)
    : AbilityScript(ability, caster, target)
{
    private const float ProjectileSpeed = 5f;

    private static readonly Vector3 AbilityHeightOffset = new(0, 0.5f, 0);
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }

    public override object State { get; set; } = SpellState.Executing;

    protected override bool ShouldRun() => true;

    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.SpellProjectile, IObject.GenerateId());
        Position = Caster.Position + AbilityHeightOffset;
        if (Target == null)
        {
            logger.LogWarning("Fireball ability has no target");
            State = SpellState.Finished;
        }

        Velocity = Vector3.Normalize(Target!.Position + AbilityHeightOffset - Position);
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (State is SpellState.Finished)
        {
            return;
        }

        if (Vector3.Distance(Position, Target!.Position + AbilityHeightOffset) < 0.1f)
        {
            logger.LogInformation("Ability {AbilityId} hit {CreatureId}", Ability.AbilityId, Target.Guid);
            Target.OnHit(Caster, Ability.Metadata.EffectValue);
            State = SpellState.Finished;
            return;
        }

        Velocity = Vector3.Normalize(Target.Position + AbilityHeightOffset - Position);
        Position += Velocity * ProjectileSpeed * (float)deltaTime.TotalSeconds;
    }

    public override AbilityScript Clone()
    {
        FireballAbilityScript clone = new(logger, Ability, Caster, Target)
        {
            Guid = Guid, Position = Position, Velocity = Velocity, Orientation = Orientation
        };

        return clone;
    }
}
