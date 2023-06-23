using System;
using Avalon.Client.Managers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Models;

public class Hero : IDisposable
{
    private const int FrameWidth = 32;
    private const int FrameHeight = 32;
    private const int FrameCount = 3;
    private const float FrameTime = 0.2f;
    
    private const float SPEED = 100f;
    
    public Vector2 Position;

    private Vector2 _minPos;
    private Vector2 _maxPos;

    private int _currentFrame;
    
    private float _elapsedTime;
    private Rectangle _debugRect;
    private Texture2D _debugTexture;
    private readonly Sprite _sprite;

    public Hero(Texture2D texture, Vector2 position)
    {
        Position = position;
        _sprite = new Sprite(texture, position);
        _sprite.Origin = new Vector2(FrameWidth / 2f, FrameHeight / 2f);

        CreateDebugBorder();
    }

    public void SetBounds(Point mapSize, Point tileSize)
    {
        _minPos = new Vector2((-tileSize.X / 2f), (-tileSize.Y / 2f));
        _maxPos = new Vector2(mapSize.X - (tileSize.X / 2), mapSize.Y - (tileSize.X / 2));
    }

    public void Update(Func<Rectangle, bool> collisionCheckingFunction)
    {
        var previousPosition = Position;
        
        Position += InputManager.Direction * Globals.Time * SPEED;
        Position = Vector2.Clamp(Position, _minPos, _maxPos);

        // Calculate the rectangle that encompasses the sprite with the border
        _debugRect = new Rectangle(
            (int)(Position.X),
            (int)(Position.Y),
            32,
            32
        );

        if (collisionCheckingFunction(_debugRect))
        {
            Position = previousPosition;
            _debugRect = new Rectangle(
                (int)(Position.X),
                (int)(Position.Y),
                32,
                32
            );
        }

        _sprite.Position = Position;

        UpdateAnimation();
    }
    
    private void CreateDebugBorder()
    {
        // Draw the border using a 1x1 pixel white texture
        _debugTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        _debugTexture.SetData(new[] { Color.White });
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (false)
        {
            spriteBatch.Draw(_debugTexture, _debugRect, Color.Black);
        }
        
        // Calculate the source rectangle based on the current frame and direction
        var sourceRect = new Rectangle(_currentFrame * FrameWidth, (int)InputManager.MovementDirection * FrameHeight, FrameWidth, FrameHeight);
        
        spriteBatch.Draw(_sprite.Texture, _sprite.Position, sourceRect, Color.White, 0f, _sprite.Origin, _sprite.Scale, SpriteEffects.None, 0);
    }

    private void UpdateAnimation()
    {
        // Only animate if we're moving
        if (InputManager.Direction != Vector2.Zero)
        {
            _elapsedTime += Globals.Time;
        
            if (_elapsedTime >= FrameTime)
            {
                _currentFrame++;
                if (_currentFrame >= FrameCount)
                {
                    _currentFrame = 0;
                }
                _elapsedTime = 0;
            }
        }
    }
    
    public void SetPosition(Vector2 position)
    {
        Position = position;
        _sprite.Position = position;
    }

    public void Dispose()
    {
        _debugTexture?.Dispose();
        _sprite?.Dispose();
    }
}
