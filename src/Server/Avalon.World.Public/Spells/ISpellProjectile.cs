using Avalon.Common.Mathematics;
using Avalon.World.Public.Characters;

namespace Avalon.World.Public.Spells;

public interface ISpellProjectile
{
    static readonly Vector3 HeightOffset = new(0, 0.5f, 0);
    
    ulong Id { get; set; }
    ISpell Spell { get; set; }
    ICharacter Caster { get; set; }
    IUnit Target { get; set; }
    Vector3 Position { get; set; }
    float Speed { get; set; }
    Vector3 Velocity { get; set; }
    
    void Update(TimeSpan deltaTime);
}
