using System.Drawing;
using System.Numerics;
using Avalon.Common.Extensions;
using Avalon.Database.Characters;
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
    private Random _random = new();

    public UrielTownPatrolScript(Creature creature, MapInstance map) : base(creature, map)
    {
        Creature.Velocity = new Vector2(1, 0);
        _state = State.Moving;
    }

    public override void Update(TimeSpan deltaTime)
    {
        Creature.Position += Creature.Velocity * Creature.Speed * (float)deltaTime.TotalSeconds;
        Creature.Bounds = new Rectangle(Creature.Position.ToPoint(), Creature.Bounds.Size);

        if (Map.VirtualizedMap.IsObjectColliding(Creature.Bounds))
        {
            MoveAwayFromCollision();
            InvertDirection();
        }
    }

    public override void OnCharacterInteraction(Character character)
    {
        Console.WriteLine($"Uriel: Hello {character.Name}!");
    }

    private void MoveAwayFromCollision()
    {
        Creature.Position -= Creature.Velocity * Creature.Speed / 15f; // You may want to use a smaller value here instead of Creature.Speed to prevent the creature from moving too far away from the collision.
    }

    private void InvertDirection()
    {
        var directions = new Vector2[] {
            new Vector2(0, -1),    // Up
            new Vector2(1, 0),     // Right
            new Vector2(0, 1),     // Down
            new Vector2(-1, 0),    // Left
            new Vector2(-1, -1).Normalized(), // Up-Left
            new Vector2(1, -1).Normalized(),  // Up-Right
            new Vector2(-1, 1).Normalized(),  // Down-Left
            new Vector2(1, 1).Normalized()    // Down-Right
        };
        
        var index = _random.Next(directions.Length);
        Creature.Velocity = directions[index];
    }
}
