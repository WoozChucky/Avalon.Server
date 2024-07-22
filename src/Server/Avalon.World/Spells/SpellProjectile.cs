using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Spells;

namespace Avalon.World.Spells;

public class SpellProjectile : ISpellProjectile
{
    public ulong Id { get; set; }
    public ISpell Spell { get; set; }
    public ICharacter Caster { get; set; }
    public IUnit Target { get; set; }
    public Vector3 Position { get; set; }
    public float Speed { get; set; }
    public Vector3 Velocity { get; set; }
        
    public void Update(TimeSpan deltaTime)
    {
        Velocity = Vector3.Normalize(Target.Position + ISpellProjectile.HeightOffset - Position);
        Position += Velocity * Speed * (float) deltaTime.TotalSeconds;
    }
}
