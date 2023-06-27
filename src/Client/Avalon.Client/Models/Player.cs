using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Models;

public class Player
{
    public Guid Id { get; set; }
    public Rectangle BoundingBox;

    private const int FrameWidth = 32;
    private const int FrameHeight = 32;
    private const int FrameCount = 3;
    private const float FrameTime = 0.2f;
    private int _currentFrame;
    private float _elapsedTime;

    private int direction = 0;
    
    private Vector2 currentPosition;
    private Vector2 previousPosition;
    private Vector2 predictedPosition;
    
    private Vector2 nextPosition;
    private Vector2 velocity;
    private float interpolationTime;
    private float predictionTime = 0.05f;
    
    private Texture2D _debugTexture;
    private Rectangle _debugRect;

    private readonly Sprite _sprite;
    
    public Player(Texture2D texture, Vector2 position, bool debug = false)
    {
        currentPosition = position;
        previousPosition = position;
        
        _sprite = new Sprite(texture, position, debug: true);
        _sprite.Origin = new Vector2(FrameWidth / 2f, FrameHeight / 2f);
        _sprite.Debug = debug;

        BoundingBox = new Rectangle(
            (int)(currentPosition.X),
            (int)(currentPosition.Y),
            FrameWidth,
            FrameHeight
        );

        CreateDebugBorder();
    }

    private void CreateDebugBorder()
    {
        _debugRect = new Rectangle(
            (int)(currentPosition.X - _sprite.Origin.X),
            (int)(currentPosition.Y - _sprite.Origin.Y),
            FrameWidth,
            FrameHeight
        );

        // Draw the border using a 1x1 pixel white texture
        _debugTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        _debugTexture.SetData(new[] { Color.White });
    }
    
    public void UpdatePosition(Vector2 newPosition, float timeSinceLastUpdate)
    {
        previousPosition = currentPosition;
        currentPosition = newPosition;
        interpolationTime = 0f;
        
        predictedPosition = currentPosition + (velocity * predictionTime);
    }

    public void UpdateVelocity(Vector2 newVelocity)
    {
        velocity = newVelocity;
    }

    public void Update(float deltaTime)
    {
        interpolationTime += deltaTime;
        
        BoundingBox.X = (int)(currentPosition.X);
        BoundingBox.Y = (int)(currentPosition.Y);

        _debugRect.X = (int)(currentPosition.X - _sprite.Origin.X);
        _debugRect.Y = (int)(currentPosition.Y - _sprite.Origin.Y);
        
        // Interpolate between the previous and current positions, and between the current and predicted positions.
        var interpolatedPosition = InterpolatePosition(previousPosition, currentPosition, interpolationTime);
        _sprite.Position = InterpolatePosition(currentPosition, predictedPosition, interpolationTime);

        _sprite.Update();

        UpdateAnimation();
    }
    
    private Vector2 InterpolatePosition(Vector2 startPosition, Vector2 endPosition, float alpha)
    {
        return startPosition + alpha * (endPosition - startPosition);
    }

    private void UpdateAnimation()
    {
        if (velocity != Vector2.Zero)
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

    public void Draw(SpriteBatch spriteBatch)
    {
        if (velocity.Y < 0)
        {
            // up
            direction = 3;
        }
        else if (velocity.Y > 0)
        {
            // down
            direction = 0;
        }
        else if (velocity.X < 0)
        {
            // left
            direction = 1;
        }
        else if (velocity.X > 0)
        {
            // right
            direction = 2;
        }

        if (_sprite.Debug)
        {
            spriteBatch.Draw(_debugTexture, _debugRect, Color.Black);
        }

        var sourceRect = new Rectangle(_currentFrame * FrameWidth, direction * FrameHeight, FrameWidth, FrameHeight);
        
        spriteBatch.Draw(_sprite.Texture, _sprite.Position, sourceRect, Color.White, 0f, _sprite.Origin, _sprite.Scale, SpriteEffects.None, 0);
    }
}
