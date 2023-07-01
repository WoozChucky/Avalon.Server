using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Models;

public class CameraFollowingSprite : Sprite
{
    public CameraFollowingSprite(Texture2D texture, Vector2 position, float scale = 1f, bool debug = false)
    : base(texture, position, scale, debug)
    {
    }

    public override void Update(float deltaTime)
    {
        
        base.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (Debug)
        {
            spriteBatch.Draw(OutlineTexture, OutlineRect, Color.Black);
        }
        spriteBatch.Draw(Texture, Position + Globals.CameraPosition, null, Color.White, 0f, Origin, Scale, SpriteEffects.None, 0f);
    }

    public override void Dispose()
    {
        Texture?.Dispose();
        OutlineTexture?.Dispose();
    }
}
