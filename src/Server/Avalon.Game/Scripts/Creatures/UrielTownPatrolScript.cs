using System.Drawing;
using System.Numerics;
using Avalon.Common.Extensions;
using Avalon.Game.Creatures;
using Avalon.Game.Maps;

namespace Avalon.Game.Scripts.Creatures;

public class UrielTownPatrolScript : AIScript
{
    private enum State
    {
        Collided,
        Moving
    }

    private State _state;
    
    
    public UrielTownPatrolScript(Creature creature, MapInstance map) : base(creature, map)
    {
        Creature.Velocity = new Vector2(1, 0);
        _state = State.Moving;
    }

    public override void Update(TimeSpan deltaTime)
    {
        Creature.Position += Creature.Velocity * Creature.Speed * (float)deltaTime.TotalSeconds;
        Creature.Bounds = new Rectangle(Creature.Position.ToPoint(), new Size(32, 32));
        
        Console.WriteLine("Uriel is moving to {0}", Creature.Position);
        
        if (Map.VirtualizedMap.IsObjectColliding(Creature.Bounds))
        {
            InvertDirection();
            Console.WriteLine($"Uriel collided with object at {Creature.Position}");
        }
    }

    private void InvertDirection()
    {
        Creature.Velocity = -Creature.Velocity;
    }
}
