using System;
using System.Linq;
using Silk.NET.Input;

namespace Avalon.Client.Native.Graphics;

public sealed class InputManager : IDisposable
{
    public static InputManager Instance { get; } = new InputManager();
    
    private IInputContext _inputContext = null!;
    private IMouse? _primaryMouse;
    private IKeyboard? _primaryKeyboard;
    private IGamepad? _primaryGamepad;
    
    public bool HasMouseAndKeyboard => _primaryMouse != null && _primaryKeyboard != null;
    public bool HasGamepad => _primaryGamepad != null;
    public bool IsMouseCaptured => HasMouseAndKeyboard && _primaryMouse!.Cursor.CursorMode == CursorMode.Disabled;
    
    public void Initialize(IInputContext inputContext)
    {
        _inputContext = inputContext;
        _primaryMouse = inputContext.Mice.Any() ? inputContext.Mice[0] : null;
        _primaryKeyboard = inputContext.Keyboards.Any() ? inputContext.Keyboards[0] : null;
        _primaryGamepad = inputContext.Gamepads.Any() ? inputContext.Gamepads[0] : null;
        
        _inputContext.ConnectionChanged += InputOnConnectionChanged;
    }
    
    private void InputOnConnectionChanged(IInputDevice device, bool connected)
    {
        switch (device)
        {
            case IGamepad gamepad:
            {
                _primaryGamepad = gamepad;
                Console.WriteLine($"Gamepad {gamepad.Name} {(connected ? "connected" : "disconnected")}!");
                if (connected)
                {
                    gamepad.ThumbstickMoved += GamepadOnThumbstickMoved;
                }
                else
                {
                    gamepad.ThumbstickMoved -= GamepadOnThumbstickMoved;
                }

                break;
            }
            case IMouse mouse:
                _primaryMouse = mouse;
                Console.WriteLine($"Mouse {mouse.Name} {(connected ? "connected" : "disconnected")}!");
                break;
            case IKeyboard keyboard:
                _primaryKeyboard = keyboard;
                Console.WriteLine($"Keyboard {keyboard.Name} {(connected ? "connected" : "disconnected")}!");
                break;
        }
    }

    private void GamepadOnThumbstickMoved(IGamepad arg1, Thumbstick arg2)
    {
        
    }

    public void Dispose()
    {
        _inputContext.ConnectionChanged -= InputOnConnectionChanged;
        _inputContext.Dispose();
    }
}
