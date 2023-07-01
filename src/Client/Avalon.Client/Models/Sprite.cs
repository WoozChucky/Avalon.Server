using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Models;

public class Sprite : IDisposable
{
    protected Texture2D OutlineTexture;
    protected Rectangle OutlineRect;
    
    public bool Debug { get; set; }
    public Vector2 Position { get; set; }
    public Vector2 Origin { get; set; }
    public float Scale { get; protected set; }
    public Texture2D Texture { get; set; }
    
    
    public Sprite(Texture2D texture, Vector2 position, float scale = 1f, bool debug = false)
    {
        Texture = texture;
        Debug = debug;
        Position = position;
        Origin = new Vector2(Texture.Width / 2f, Texture.Height / 2f);
        Scale = scale;
        
        CreateDebugBorder();
    }

    private void CreateDebugBorder()
    {
        // Calculate the rectangle that encompasses the sprite with the border
        OutlineRect = new Rectangle(
            (int)(Position.X - Origin.X * Scale) - 1,
            (int)(Position.Y - Origin.Y * Scale) - 1,
            (int)(Texture.Width * Scale) + 2,
            (int)(Texture.Height * Scale) + 2
        );

        // Draw the border using a 1x1 pixel white texture
        OutlineTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        OutlineTexture.SetData(new[] { Color.White });
    }
    
    public virtual void Update(float deltaTime)
    {
        if (Debug)
        {
            OutlineRect.X = (int)(Position.X - Origin.X * Scale) - 1;
            OutlineRect.Y = (int)(Position.Y - Origin.Y * Scale) - 1;
        }
    }

    public virtual void Draw(SpriteBatch spriteBatch)
    {
        if (Debug)
        {
            spriteBatch.Draw(OutlineTexture, OutlineRect, Color.Black);
        }
        spriteBatch.Draw(Texture, Position, null, Color.White, 0f, Origin, Scale, SpriteEffects.None, 0f);
    }

    public virtual void Dispose()
    {
        Texture?.Dispose();
        OutlineTexture?.Dispose();
    }
}
