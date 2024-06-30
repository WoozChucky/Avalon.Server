using System.Drawing;
using System.Numerics;
using Avalon.Common.Extensions;
using Avalon.Database.Characters;
using Avalon.Domain.Characters;
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
    
    private const int Up = 0;
    private const int Right = 1;
    private const int Down = 2;
    private const int Left = 3;
    
    private static readonly Vector2[] Directions =
    [
        new Vector2(0, -1),    // Up
        new Vector2(1, 0),     // Right
        new Vector2(0, 1),     // Down
        new Vector2(-1, 0),    // Left
        new Vector2(-1, -1).Normalized(), // Up-Left
        new Vector2(1, -1).Normalized(),  // Up-Right
        new Vector2(-1, 1).Normalized(),  // Down-Left
        new Vector2(1, 1).Normalized()    // Down-Right
    ];

    public UrielTownPatrolScript(Creature creature, MapInstance map) : base(creature, map)
    {
        Creature.Velocity = Directions[Right];
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
        var currentDirection = Creature.Velocity;
        
        if (currentDirection == Directions[0]) // Up
        {
            Creature.Velocity = Directions[2]; // Down
        }
        else if (currentDirection == Directions[1]) // Right
        {
            Creature.Velocity = Directions[3]; // Left
        }
        else if (currentDirection == Directions[2]) // Down
        {
            Creature.Velocity = Directions[0]; // Up
        }
        else if (currentDirection == Directions[3]) // Left
        {
            Creature.Velocity = Directions[1]; // Right
        }
        else if (currentDirection == Directions[4]) // Up-Left
        {
            Creature.Velocity = Directions[6]; // Down-Left
        }
        else if (currentDirection == Directions[5]) // Up-Right
        {
            Creature.Velocity = Directions[7]; // Down-Right
        }
        else if (currentDirection == Directions[6]) // Down-Left
        {
            Creature.Velocity = Directions[4]; // Up-Left
        }
        else if (currentDirection == Directions[7]) // Down-Right
        {
            Creature.Velocity = Directions[5]; // Up-Right
        }
        
        Console.WriteLine("Uriel: I've hit a wall! I'm going to change direction.");
    }
}
