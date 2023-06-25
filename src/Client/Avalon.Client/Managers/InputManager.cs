using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Avalon.Client.Managers;

public enum MovementDirection
{
    Down,
    Left,
    Right,
    Up
}

public static class InputManager
{
    public static Vector2 Direction;
    public static MovementDirection MovementDirection;
    public static bool IsRunning = false;

    public static void Update()
    {
        Direction = Vector2.Zero;
        
        var keyboardState = Keyboard.GetState();

        if (keyboardState.GetPressedKeyCount() > 0)
        {
            IsRunning = keyboardState.IsKeyDown(Keys.Space);
            
            if (keyboardState.IsKeyDown(Keys.W) || keyboardState.IsKeyDown(Keys.Up))
            {
                Direction.Y -= 1;
                MovementDirection = MovementDirection.Up;
            }
            if (keyboardState.IsKeyDown(Keys.S) || keyboardState.IsKeyDown(Keys.Down))
            {
                Direction.Y += 1;
                MovementDirection = MovementDirection.Down;
            }

            if (keyboardState.IsKeyDown(Keys.A) || keyboardState.IsKeyDown(Keys.Left))
            {
                Direction.X -= 1;
                MovementDirection = MovementDirection.Left;
            }

            if (keyboardState.IsKeyDown(Keys.D) || keyboardState.IsKeyDown(Keys.Right))
            {
                Direction.X += 1;
                MovementDirection = MovementDirection.Right;
            }
        }
        
        if (Direction != Vector2.Zero)
            Direction.Normalize();
    }
}
