using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Models;

public class Player
{
    public Guid Id { get; set; }
    
    private const int FrameWidth = 32;
    private const int FrameHeight = 32;
    private const int FrameCount = 3;
    private const float FrameTime = 0.2f;
    private int _currentFrame;
    private float _elapsedTime;

    private int direction = 0;
    
    private Vector2 currentPosition;
    private Vector2 previousPosition;
    private Vector2 nextPosition;
    private Vector2 velocity;
    private float interpolationTime;
    private float smoothingWeight = 0.1f;
    private float predictionTime = 0.25f;
    
    
    private readonly Sprite _sprite;
    
    public Player(Texture2D texture, Vector2 position)
    {
        currentPosition = position;
        previousPosition = position;
        
        _sprite = new Sprite(texture, position);
        _sprite.Origin = new Vector2(32 / 2f, 32 / 2f);
    }

    public void Test(Vector2 pos)
    {
        _sprite.Position = pos;
    }
    
    public void UpdatePosition(Vector2 newPosition)
    {
        previousPosition = currentPosition;
        currentPosition = newPosition;
        interpolationTime = 0f;
    }

    public void UpdateVelocity(Vector2 newVelocity)
    {
        velocity = newVelocity;
    }

    public void Update(float deltaTime)
    {
        interpolationTime += deltaTime;
        nextPosition = PredictPosition(currentPosition, velocity, predictionTime);

        UpdateAnimation();
    }

    public Vector2 GetInterpolatedPosition()
    {
        return SmoothInterpolation(previousPosition, currentPosition, nextPosition, smoothingWeight);
    }

    private Vector2 SmoothInterpolation(Vector2 previousPosition, Vector2 currentPosition, Vector2 nextPosition, float weight)
    {
        return (previousPosition * (1 - weight)) + (currentPosition * weight);
    }

    private Vector2 PredictPosition(Vector2 currentPosition, Vector2 velocity, float predictionTime)
    {
        return currentPosition + (velocity * predictionTime);
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

        var sourceRect = new Rectangle(_currentFrame * FrameWidth, direction * FrameHeight, FrameWidth, FrameHeight);
        
        spriteBatch.Draw(_sprite.Texture, _sprite.Position, sourceRect, Color.White, 0f, _sprite.Origin, _sprite.Scale, SpriteEffects.None, 0);
    }
}
