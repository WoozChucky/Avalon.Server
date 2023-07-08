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

        if (Map.VirtualizedMap.IsObjectColliding(Creature.Bounds))
        {
            MoveAwayFromCollision();
            InvertDirection();
        }
    }

    private void MoveAwayFromCollision()
    {
        Creature.Position -= Creature.Velocity * Creature.Speed / 15f; // You may want to use a smaller value here instead of Creature.Speed to prevent the creature from moving too far away from the collision.
    }

    private void InvertDirection()
    {
        var theta = new Random().NextDouble() * 2 * Math.PI;
        Creature.Velocity = new Vector2((float)Math.Cos(theta), (float)Math.Sin(theta));
    }
}
