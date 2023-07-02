using System;
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
    private MouseState previousMouseState;
    private bool isLeftButtonClicked;
    private bool isRightButtonClicked;
    
    private static InputManager _instance;
    
    public static InputManager Instance => _instance ??= new InputManager();
    public Vector2 MousePosition { get; private set; }

    private InputManager()
    {
        previousMouseState = Mouse.GetState();
        isLeftButtonClicked = false;
        isRightButtonClicked = false;
    }

    public void Update(float delta)
    {
        _previousState = _currentState;
        _currentState = Keyboard.GetState();
        
        var currentMouseState = Mouse.GetState();
        MousePosition = new Vector2(currentMouseState.X, currentMouseState.Y);

        if (currentMouseState.LeftButton == ButtonState.Released && previousMouseState.LeftButton == ButtonState.Pressed)
        {
            isLeftButtonClicked = true;
        }
        else
        {
            isLeftButtonClicked = false;
        }
        
        if (currentMouseState.RightButton == ButtonState.Released && previousMouseState.RightButton == ButtonState.Pressed)
        {
            isRightButtonClicked = true;
        }
        else
        {
            isRightButtonClicked = false;
        }

        previousMouseState = currentMouseState;
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
    
    public bool IsLeftButtonClicked()
    {
        return isLeftButtonClicked;
    }
    
    public bool IsRightButtonClicked()
    {
        return isLeftButtonClicked;
    }

    public bool IsMouseOverRectangle(Rectangle rectangle, bool useCamera = false)
    {
        var currentMouseState = Mouse.GetState();
        var mousePosition = new Point(currentMouseState.X, currentMouseState.Y);

        //if (useCamera)
        //{
        //    mousePosition.X += (int) Globals.CameraPosition.X;
        //    mousePosition.Y += (int) Globals.CameraPosition.Y;
        //}
        
        //Console.WriteLine(mousePosition);
        
        return rectangle.Contains(mousePosition);
    }
}
