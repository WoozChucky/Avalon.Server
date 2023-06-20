using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Models;

public class Sprite : IDisposable
{
    public Texture2D Texture;
    private readonly bool _debug;
    
    private Texture2D _outlineTexture;
    private Rectangle _outlineRect;
    
    public Vector2 Position { get; set; }
    public Vector2 Origin { get; set; }
    public float Scale { get; protected set; }
    
    
    public Sprite(Texture2D texture, Vector2 position, float scale = 1f, bool debug = false)
    {
        Texture = texture;
        _debug = debug;
        Position = position;
        Origin = new Vector2(Texture.Width / 2f, Texture.Height / 2f);
        Scale = scale;
        
        if (_debug)
        {
            CreateDebugBorder();
        }
    }

    private void CreateDebugBorder()
    {
        // Calculate the rectangle that encompasses the sprite with the border
        _outlineRect = new Rectangle(
            (int)(Position.X - Origin.X * Scale) - 1,
            (int)(Position.Y - Origin.Y * Scale) - 1,
            (int)(Texture.Width * Scale) + 2,
            (int)(Texture.Height * Scale) + 2
        );

        // Draw the border using a 1x1 pixel white texture
        _outlineTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        _outlineTexture.SetData(new[] { Color.White });
    }
    
    public virtual void Update()
    {
    }

    public virtual void Draw(SpriteBatch spriteBatch)
    {
        if (_debug)
        {
            spriteBatch.Draw(_outlineTexture, _outlineRect, Color.Black);
        }
        spriteBatch.Draw(Texture, Position, null, Color.White, 0f, Origin, Scale, SpriteEffects.None, 0f);
    }

    public void Dispose()
    {
        Texture?.Dispose();
        _outlineTexture?.Dispose();
    }
}
