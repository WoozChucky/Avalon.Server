using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Avalon.Client.Managers;

public class MouseManager
{
    private static MouseManager _instance;
    
    public static MouseManager Instance => _instance ??= new MouseManager();
    
    private MouseState previousMouseState;
    private bool isLeftButtonClicked;

    public MouseManager()
    {
        previousMouseState = Mouse.GetState();
        isLeftButtonClicked = false;
    }

    public void Update()
    {
        MouseState currentMouseState = Mouse.GetState();

        if (currentMouseState.LeftButton == ButtonState.Released && previousMouseState.LeftButton == ButtonState.Pressed)
        {
            isLeftButtonClicked = true;
        }
        else
        {
            isLeftButtonClicked = false;
        }

        previousMouseState = currentMouseState;
    }

    public bool IsLeftButtonClicked()
    {
        return isLeftButtonClicked;
    }

    public bool IsMouseOverRectangle(Rectangle rectangle)
    {
        var currentMouseState = Mouse.GetState();
        var mousePosition = new Point(currentMouseState.X, currentMouseState.Y);

        return rectangle.Contains(mousePosition);
    }
}
