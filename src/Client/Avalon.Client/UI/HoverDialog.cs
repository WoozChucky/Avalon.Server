using System;
using Avalon.Client.Managers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI;

public class HoverDialog : IGameComponent
{
    private readonly SpriteFont _font;
    private readonly string _text;
    private readonly float _padding;
    private bool _isVisible;
    private Texture2D _backgroundTexture;
    private Vector2 _backgroundPosition;
    private Vector2 _textPosition;
    private Vector2 _textShadowPosition;

    public HoverDialog(SpriteFont font, string text, float padding = 10f)
    {
        _font = font;
        _text = text;
        _padding = padding;
        _isVisible = false;
        
        CreateBackgroundTexture();
    }
    
    private void CreateBackgroundTexture()
    {
        var textSize = _font.MeasureString(_text);
        var width = (int) (textSize.X + 2 * _padding);
        var height = (int) (textSize.Y + 2 * _padding);
        
        const int borderWidth = 2;
        
        _backgroundTexture = new Texture2D(Globals.GraphicsDevice, width, height);
        var backgroundColor = new Color[width * height];
        
        var bgColor = new Color(195, 172, 144, 255);
        var borderColor = new Color(181, 117, 77);
        
        // Fill the entire texture with a semi-transparent red color
        for (var i = 0; i < backgroundColor.Length; i++)
        {
            backgroundColor[i] = bgColor;
        }

        // Set the top and bottom border pixels to black
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < borderWidth; y++)
            {
                var topIndex = y * width + x;
                var bottomIndex = (height - 1 - y) * width + x;
                backgroundColor[topIndex] = borderColor;
                backgroundColor[bottomIndex] = borderColor;
            }
        }
        
        // Set the left and right border pixels to black
        for (var y = borderWidth; y < height - borderWidth; y++)
        {
            for (var x = 0; x < borderWidth; x++)
            {
                var leftIndex = y * width + x;
                var rightIndex = y * width + (width - 1 - x);
                backgroundColor[leftIndex] = borderColor;
                backgroundColor[rightIndex] = borderColor;
            }
        }
        
        _backgroundTexture.SetData(backgroundColor);
        
        _backgroundPosition = Vector2.Zero;
    }
    
    public void Dispose()
    {
        _backgroundTexture.Dispose();
    }

    public void Update(float deltaTime)
    {
        if (!_isVisible)
        {
            return;
        }

        _backgroundPosition = new Vector2(
            InputManager.Instance.MousePosition.X - _backgroundTexture.Width - 40,
            InputManager.Instance.MousePosition.Y - _backgroundTexture.Height / 2f - 10
        );
        
        _textPosition = new Vector2(Globals.CameraPosition.X + _backgroundPosition.X + _padding, Globals.CameraPosition.Y + _backgroundPosition.Y + _padding);
        _textShadowPosition = new Vector2(Globals.CameraPosition.X + _backgroundPosition.X + _padding, Globals.CameraPosition.Y + _backgroundPosition.Y + _padding) + new Vector2(2, 2);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!_isVisible)
        {
            return;
        }
        
        // Draw chat background
        spriteBatch.Draw(_backgroundTexture, Globals.CameraPosition + _backgroundPosition, Color.White);
        
        // Draw the text
        spriteBatch.DrawString(_font, _text, _textShadowPosition, Color.Black);
        spriteBatch.DrawString(_font, _text, _textPosition, Color.White);
    }

    public void ToggleVisibility()
    {
        _isVisible = !_isVisible;
    }

    public void SetVisible(bool visible)
    {
        _isVisible = visible;
    }
}
