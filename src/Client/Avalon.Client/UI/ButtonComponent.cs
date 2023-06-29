using System;
using Avalon.Client.Managers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI;

public class ButtonComponent : IDisposable
{
    private Texture2D texture;
    private Rectangle bounds;
    private Color defaultColor;
    private Color hoverColor;
    private Color currentColor;

    private bool isHovered;
    private bool isClicked;
    
    public delegate void ButtonClickHandler(ButtonComponent button);

    public event ButtonClickHandler Clicked;

    public ButtonComponent(Texture2D texture, Vector2 position, Color defaultColor, Color hoverColor)
    {
        this.texture = texture;
        this.bounds = new Rectangle(position.ToPoint(), texture.Bounds.Size);
        this.defaultColor = defaultColor;
        this.hoverColor = hoverColor;

        currentColor = defaultColor;
        isHovered = false;
        isClicked = false;
    }

    public void Update()
    {
        isHovered = MouseManager.Instance.IsMouseOverRectangle(bounds);

        if (isHovered && MouseManager.Instance.IsLeftButtonClicked())
        {
            isClicked = true;
            Clicked?.Invoke(this);
        }
        else
        {
            isClicked = false;
        }

        currentColor = isHovered ? hoverColor : defaultColor;
    }

    public bool IsClicked()
    {
        return isClicked;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(texture, bounds, currentColor);
    }

    public void Dispose()
    {
        texture.Dispose();
    }
}
