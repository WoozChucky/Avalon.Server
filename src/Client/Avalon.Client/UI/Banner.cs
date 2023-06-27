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
    private readonly Vector2 _size;

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
        _size = Vector2.Zero;
    }

    public Banner(Vector2 position, Vector2 size, string text = "", float alpha = 1f)
    {
        Text = text;
        _scale = 1f;
        _alpha = alpha;
        _position = position;
        _color = new Color(1.0f, 1.0f, 1.0f, _alpha);
        _size = size;
    }
    
    public virtual void Load()
    {
        _texture = Globals.Content.Load<Texture2D>("Images/Label");
        _font = Globals.Content.Load<SpriteFont>("Fonts/Nintendo");
        
        _originalSize = new Vector2(_texture.Width, _texture.Height);
        
        

        if (_scale != 1f)
        {
            var scaledSize = _originalSize * _scale; // Calculate the scaled size
            _origin = scaledSize / 2f;

            _destinationRect = new Rectangle(
                (int)(_position.X - _origin.X) + (int) Globals.CameraPosition.X, 
                (int)(_position.Y - _origin.Y) + (int) Globals.CameraPosition.Y, 
                (int)(scaledSize.X), 
                (int)(scaledSize.Y)
            );
        }
        else
        {
            _origin = _size / 2f;

            _destinationRect = new Rectangle(
                (int)(_position.X - _origin.X) + (int) Globals.CameraPosition.X, 
                (int)(_position.Y - _origin.Y) + (int) Globals.CameraPosition.Y, 
                (int)(_size.X), 
                (int)(_size.Y)
            );
        }
        
        
        
    }

    public virtual void Unload()
    {
        _texture?.Dispose();
    }

    public virtual void Update(GameTime gameTime)
    {
        _destinationRect.X = (int)(_position.X - _origin.X) + (int) Globals.CameraPosition.X;
        _destinationRect.Y = (int)(_position.Y - _origin.Y) + (int) Globals.CameraPosition.Y;
    }

    public virtual void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(_texture, _destinationRect, _color);
        if (!string.IsNullOrEmpty(Text))
            spriteBatch.DrawString(_font, Text, _position + Globals.CameraPosition, Color.Black, 0f, _font.MeasureString(Text) / 2f, 1f, SpriteEffects.None, 0f);
    }
}
