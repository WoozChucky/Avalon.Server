using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Avalon.Client.Managers;

public enum MovementDirection
{
    Down,
    Left,
    Right,
    Up,
    Idle
}

public class InputManager
{
    private KeyboardState _previousState;
    private KeyboardState _currentState;
    
    private static InputManager _instance;
    
    public static InputManager Instance => _instance ??= new InputManager();

    public void Update(float delta)
    {
        _previousState = _currentState;
        _currentState = Keyboard.GetState();
    }
    
    public bool KeyPressed(params Keys[] keys)
    {
        return keys.Any(key => _currentState.IsKeyDown(key) && _previousState.IsKeyUp(key));
    }
    
    public bool KeyReleased(params Keys[] keys)
    {
        return keys.Any(key => _currentState.IsKeyUp(key) && _previousState.IsKeyDown(key));
    }
    
    public bool KeyDown(params Keys[] keys)
    {
        return keys.Any(key => _currentState.IsKeyDown(key));
    }

    public Keys GetKeyPressed(params Keys[] keys)
    {
        return keys.FirstOrDefault(key => _currentState.IsKeyDown(key) && _previousState.IsKeyUp(key));
    }
}

/*
public static class InputManager
{
    public static Vector2 Direction;
    public static MovementDirection MovementDirection;
    public static bool IsRunning = false;
    public static bool ShowingMetrics { get; set; } = true;
    public static bool PreviousDebugGraphics { get; set; } = false;
    public static bool DebugGraphics { get; set; } = true;
    
    private static KeyboardState _previousKeyboardState;

    public static void Update()
    {
        Direction = Vector2.Zero;
        
        
        var currentKeyboardState = Keyboard.GetState();

        if (currentKeyboardState.IsKeyDown(Keys.F1) && _previousKeyboardState.IsKeyUp(Keys.F1))
        {
            ShowingMetrics = !ShowingMetrics;
        }

        if (currentKeyboardState.IsKeyDown(Keys.F3) && _previousKeyboardState.IsKeyUp(Keys.F3))
        {
            DebugGraphics = !DebugGraphics;
        }
            
        _previousKeyboardState = currentKeyboardState;
        
        
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
*/
