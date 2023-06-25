using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI;

public class Banner
{
    
    private readonly float _scale;
    private readonly float _alpha;
    private readonly Vector2 _position;
    private readonly Color _color;

    private Texture2D _texture;
    private SpriteFont _font;
    
    private Vector2 _originalSize;
    
    private Vector2 _origin;
    
    private Rectangle _destinationRect;

    public string Text { get; set; }
    
    public Banner(Vector2 position, string text = "", float scale = 1f, float alpha = 1f)
    {
        Text = text;
        _scale = scale;
        _alpha = alpha;
        _position = position;
        _color = new Color(1.0f, 1.0f, 1.0f, _alpha);
    }
    
    public virtual void Load()
    {
        _texture = Globals.Content.Load<Texture2D>("Images/Label");
        _font = Globals.Content.Load<SpriteFont>("Fonts/Nintendo");
        
        _originalSize = new Vector2(_texture.Width, _texture.Height);
        
        var scaledSize = _originalSize * _scale; // Calculate the scaled size
        
        _origin = scaledSize / 2f;
        _destinationRect = new Rectangle(
            (int)(_position.X - _origin.X), 
            (int)(_position.Y - _origin.Y), 
            (int)(scaledSize.X), 
            (int)(scaledSize.Y)
        );
    }

    public virtual void Unload()
    {
        _texture?.Dispose();
    }

    public virtual void Update(GameTime gameTime)
    {
        // TODO: Update the scene's logic
    }

    public virtual void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(_texture, _destinationRect, _color);
        if (!string.IsNullOrEmpty(Text))
            spriteBatch.DrawString(_font, Text, _position, Color.Black, 0f, _font.MeasureString(Text) / 2f, 1f, SpriteEffects.None, 0f);
    }
}
