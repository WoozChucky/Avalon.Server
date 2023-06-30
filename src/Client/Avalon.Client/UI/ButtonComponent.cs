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
    private SpriteFont font;
    private string text;

    private bool isHovered;
    private bool isClicked;
    private Vector2 textPosition;

    public delegate void ButtonClickHandler(ButtonComponent button);

    public event ButtonClickHandler Clicked;

    public ButtonComponent(Texture2D texture, Vector2 position, string text, SpriteFont font, Color defaultColor, Color hoverColor)
    {
        this.texture = texture;
        this.bounds = new Rectangle(position.ToPoint(), texture.Bounds.Size);
        this.defaultColor = defaultColor;
        this.hoverColor = hoverColor;
        this.text = text;
        this.font = font;
        currentColor = defaultColor;
        isHovered = false;
        isClicked = false;
        
        this.textPosition = bounds.Center.ToVector2() - (font.MeasureString(text) / 2f);
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

    public void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(texture, bounds, currentColor);
        
        if (!string.IsNullOrEmpty(text) && font != null)
        {
            spriteBatch.DrawString(font, text, textPosition, Color.Black);
        }
    }

    public void Dispose()
    {
        texture.Dispose();
    }
}
