using System;
using System.Collections.Concurrent;
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
    
    private const float SPEED = 80f;
    
    public Vector2 Position;
    public Rectangle BoundingBox;

    private Vector2 _minPos;
    private Vector2 _maxPos;

    private int _currentFrame;
    private float _elapsedTime;
    
    private Texture2D _debugTexture;
    private Rectangle _debugRect;
    private readonly Sprite _sprite;

    public Hero(Texture2D texture, Vector2 position)
    {
        Position = position;
        _sprite = new Sprite(texture, position);
        _sprite.Origin = new Vector2(FrameWidth / 2f, FrameHeight / 2f);
        _sprite.Debug = true;

        BoundingBox = new Rectangle(
            (int)(Position.X),
            (int)(Position.Y),
            FrameWidth,
            FrameHeight
        );

        CreateDebugBorder();
    }

    private void CreateDebugBorder()
    {
        _debugRect = new Rectangle(
            (int)(Position.X - _sprite.Origin.X),
            (int)(Position.Y - _sprite.Origin.Y),
            FrameWidth,
            FrameHeight
        );

        // Draw the border using a 1x1 pixel white texture
        _debugTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        _debugTexture.SetData(new[] { Color.White });
    }

    public void SetBounds(Point mapSize, Point tileSize)
    {
        _minPos = new Vector2((-tileSize.X / 2f), (-tileSize.Y / 2f));
        _maxPos = new Vector2(mapSize.X - (tileSize.X / 2), mapSize.Y - (tileSize.X / 2));
    }

    public void Update(Func<Rectangle, bool> collisionCheckingFunction, ConcurrentDictionary<Guid, Player> npcs,
        ConcurrentDictionary<Guid, Player> otherPlayers)
    {
        var previousPosition = Position;
        var previousBoundingBox = BoundingBox;
        
        Position += InputManager.Direction * Globals.Time * SPEED * (InputManager.IsRunning ? 1.75f : 1);
        Position = Vector2.Clamp(Position, _minPos, _maxPos);

        // Calculate the rectangle that encompasses the sprite with the border
        BoundingBox.X = (int)(Position.X);
        BoundingBox.Y = (int)(Position.Y);

        _debugRect.X = (int)(Position.X - _sprite.Origin.X);
        _debugRect.Y = (int)(Position.Y - _sprite.Origin.Y);
        
        
        // Check for collisions with NPCs
        foreach (var (id, npc) in npcs)
        {
            if (npc.BoundingBox.Intersects(BoundingBox))
            {
                Position = previousPosition;
                BoundingBox = previousBoundingBox;
            }
        }
        
        // Check for collisions with other players
        foreach (var (id, player) in otherPlayers)
        {
            if (player.BoundingBox.Intersects(BoundingBox))
            {
                Position = previousPosition;
                BoundingBox = previousBoundingBox;
            }
        }

        if (collisionCheckingFunction(BoundingBox))
        {
            Position = previousPosition;
            BoundingBox = previousBoundingBox;
        }

        _sprite.Position = Position;

        UpdateAnimation();
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (true)
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

    public void Dispose()
    {
        _debugTexture?.Dispose();
        _sprite?.Dispose();
    }
}
