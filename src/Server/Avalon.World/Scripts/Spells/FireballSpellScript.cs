using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Scripts.Spells;

public class FireballSpellScript(ILogger<FireballSpellScript> logger, ISpell spell, IUnit caster, IUnit? target)
    : SpellScript(spell, caster, target)
{
    private const float ProjectileSpeed = 5f;

    private static readonly Vector3 SpellHeightOffset = new(0, 0.5f, 0);
    public override Vector3 Position { get; set; }
    public override Vector3 Velocity { get; set; }
    public override Vector3 Orientation { get; set; }
    public override ObjectGuid Guid { get; set; }

    public override object State { get; set; } = SpellState.Executing;

    protected override bool ShouldRun() => true;

    public override void Prepare()
    {
        Guid = new ObjectGuid(ObjectType.SpellProjectile, IObject.GenerateId());
        Position = Caster.Position + SpellHeightOffset;
        if (Target == null)
        {
            logger.LogWarning("Fireball spell has no target");
            State = SpellState.Finished;
        }

        Velocity = Vector3.Normalize(Target!.Position + SpellHeightOffset - Position);
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (State is SpellState.Finished)
        {
            return;
        }

        if (Vector3.Distance(Position, Target!.Position + SpellHeightOffset) < 0.1f)
        {
            logger.LogInformation("Spell {SpellId} hit {CreatureId}", Spell.SpellId, Target.Guid);
            Target.OnHit(Caster, Spell.Metadata.EffectValue);
            State = SpellState.Finished;
            return;
        }

        Velocity = Vector3.Normalize(Target.Position + SpellHeightOffset - Position);
        Position += Velocity * ProjectileSpeed * (float)deltaTime.TotalSeconds;
    }

    public override SpellScript Clone()
    {
        FireballSpellScript clone = new(logger, Spell, Caster, Target)
        {
            Guid = Guid, Position = Position, Velocity = Velocity, Orientation = Orientation
        };

        return clone;
    }
}
