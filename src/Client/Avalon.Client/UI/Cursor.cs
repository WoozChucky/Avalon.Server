using System;
using Avalon.Client.Managers;
using Avalon.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI;

public class Cursor : IDisposable
{
    private readonly Sprite _sprite;
    
    public Cursor(Texture2D texture, bool followCamera)
    {
        if (followCamera)
        {
            _sprite = new CameraFollowingSprite(texture, Vector2.Zero);
        }
        else
        {
            _sprite = new Sprite(texture, Vector2.Zero);
            _sprite.Origin = new Vector2(0, 0);
        }
    }
    
    public void Update(float deltaTime)
    {
        _sprite.Position = InputManager.Instance.MousePosition;
    }
    
    public void Draw(SpriteBatch spriteBatch)
    {
        _sprite.Draw(spriteBatch);
    }

    public void Dispose()
    {
        _sprite.Dispose();
    }
}
