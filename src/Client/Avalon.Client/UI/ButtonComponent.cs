using System;
using Avalon.Client.Managers;
using Avalon.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI;

public class ButtonComponent : IDisposable
{
    private Sprite defaultSprite;
    private Sprite hoverSprite;
    private Sprite currentSprite;
    
    private Rectangle bounds;
    private SpriteFont font;
    private string text;

    private bool isHovered;
    private Vector2 textPosition;

    public delegate void ButtonClickHandler(ButtonComponent button);

    public event ButtonClickHandler Clicked;

    public ButtonComponent(Texture2D defaultTexture, Vector2 position, string text, SpriteFont font, Texture2D hoverTexture = null)
    {
        this.defaultSprite = new Sprite(defaultTexture, position);
        this.defaultSprite.Origin = Vector2.Zero;
        if (hoverTexture != null)
        {
            this.hoverSprite = new Sprite(hoverTexture, position);
            this.hoverSprite.Origin = Vector2.Zero;
        }
        
        this.bounds = new Rectangle(position.ToPoint(), defaultTexture.Bounds.Size);
        this.text = text;
        this.font = font;
        isHovered = false;

        if (!string.IsNullOrEmpty(this.text))
        {
            this.textPosition = bounds.Center.ToVector2() - (font.MeasureString(text) / 2f);
        }
        
        this.currentSprite = defaultSprite;
    }

    public void Update()
    {
        isHovered = InputManager.Instance.IsMouseOverRectangle(bounds);

        if (isHovered && InputManager.Instance.IsLeftButtonClicked())
        {
            Clicked?.Invoke(this);
        }

        currentSprite = isHovered && hoverSprite != null 
            ? hoverSprite 
            : defaultSprite;
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        currentSprite.Draw(spriteBatch);
        
        if (!string.IsNullOrEmpty(text) && font != null)
        {
            spriteBatch.DrawString(font, text, textPosition, Color.Black);
        }
    }

    public void Dispose()
    {
        defaultSprite.Dispose();
        hoverSprite?.Dispose();
    }
}
