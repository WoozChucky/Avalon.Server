using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Models;

public class GameCreature
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

    private readonly Guid _accountId;
    private readonly int _characterId;
    private readonly string _name;
    private Vector2 currentPosition;
    private Vector2 previousPosition;
    private Vector2 predictedPosition;
    
    private Vector2 velocity;
    private float interpolationTime;
    private float predictionTime = 0.05f;
    
    private Texture2D _debugTexture;
    private Rectangle _debugRect;

    // Store the last four positions received.
    private ConcurrentQueue<Vector2> recentPositions;
    private float _currentAngle = 0f; // represents the current direction as an angle
    
    private readonly Sprite _sprite;
    
    private readonly SpriteFont _font;
    private Vector2 _fontPosition;
    private Vector2 _fontShadowPosition;
    
    private readonly Sprite _socialSprite;

    private volatile bool _chatting;

    public GameCreature(Guid accountId, int characterId, string name, Texture2D texture, Vector2 position, bool debug = false)
    {
        _accountId = accountId;
        _characterId = characterId;
        _name = name;
        _chatting = false;
        recentPositions = new ConcurrentQueue<Vector2>();
        
        _font = Globals.Content.Load<SpriteFont>("Fonts/Default");
        _fontPosition = Vector2.Zero;
        _fontShadowPosition = Vector2.Zero;
        
        currentPosition = position;
        previousPosition = position;
        
        _sprite = new Sprite(texture, position, debug: true);
        _sprite.Origin = new Vector2(FrameWidth / 2f, FrameHeight / 2f);
        _sprite.Debug = debug;
        
        _socialSprite = new Sprite(Globals.Content.Load<Texture2D>("Images/Icons/Mail"), position);
        _socialSprite.Position = new Vector2(position.X, position.Y - 40f);

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
    
    public void OnMovementReceived(Vector2 newPosition, Vector2 newVelocity, double latency)
    {
        velocity = newVelocity;

        predictionTime = (float)latency / 1000f;

        previousPosition = currentPosition;
        currentPosition = newPosition;
        interpolationTime = 0f;
        
        predictedPosition = currentPosition + (velocity * predictionTime);

        //recentPositions.Enqueue(newPosition);
        //if (recentPositions.Count > 4)
        //{
        //    recentPositions.TryDequeue(out _);
        //}

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
    }
    public void OnChatChanged(bool chatting)
    {
        _chatting = chatting;
    }

    public void Update(float deltaTime)
    {
        interpolationTime += deltaTime;
        
        BoundingBox.X = (int)(currentPosition.X);
        BoundingBox.Y = (int)(currentPosition.Y);

        _debugRect.X = (int)(currentPosition.X - _sprite.Origin.X);
        _debugRect.Y = (int)(currentPosition.Y - _sprite.Origin.Y);
        
        _fontPosition = currentPosition + new Vector2(-(_font.MeasureString(_name).X / 2f), _font.MeasureString(_name).Y);
        _fontShadowPosition = currentPosition + new Vector2(-(_font.MeasureString(_name).X / 2f), _font.MeasureString(_name).Y) + new Vector2(2, 2);

        // Linear Interpolation
        {
            // InterpolatePosition(currentPosition, predictedPosition, interpolationTime);
        }

        // Exponential Smoothing
        {
            // Exponential smoothing between the previous and current positions, and between the current and predicted positions.
            float alpha = 0.5f;
            // Higher values of alpha will make the player's movement react more quickly to changes in the predicted position.
            // Lower values will make it smoother but also potentially more laggy.
            currentPosition = ExponentialSmoothPosition(currentPosition, predictedPosition, alpha);
            _sprite.Position = currentPosition;
            _socialSprite.Position = new Vector2(currentPosition.X, currentPosition.Y - 40f);
        }

        // Cubic Interpolation
        {
            // Perform cubic interpolation if we have enough positions.
            //if (recentPositions.Count == 4)
            //{
            //    Vector2[] positions = recentPositions.ToArray();
            //    currentPosition = CubicInterpolate(positions[0], positions[1], positions[2], positions[3], interpolationTime);
            //    _sprite.Position = currentPosition;
            //}
            //direction = (int)SmoothStep(direction, _currentAngle, deltaTime * 0.1f);
        }
        
        _sprite.Update(deltaTime);
        _socialSprite.Update(deltaTime);

        UpdateAnimation();
    }

    // Exponential Smoothing
    private Vector2 ExponentialSmoothPosition(Vector2 currentPosition, Vector2 nextPosition, float alpha)
    {
        return alpha * currentPosition + (1 - alpha) * nextPosition;
    }

    // Old Linear Interpolation
    private Vector2 InterpolatePosition(Vector2 startPosition, Vector2 endPosition, float alpha)
    {
        return startPosition + alpha * (endPosition - startPosition);
    }

    // Cubic Interpolation (Hermite Interpolation)
    private Vector2 CubicInterpolate(Vector2 y0, Vector2 y1, Vector2 y2, Vector2 y3, float mu)
    {
        Vector2 a0, a1, a2, a3, mu2;

        mu2 = new Vector2(mu * mu);
        a0 = y3 - y2 - y0 + y1;
        a1 = y0 - y1 - a0;
        a2 = y2 - y0;
        a3 = y1;

        return (a0 * mu * mu2 + a1 * mu2 + a2 * mu + a3);
    }
    
    // Smoothstep function
    private float SmoothStep(float v0, float v1, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return v0 * (2f * t3 - 3f * t2 + 1f) + v1 * (-2f * t3 + 3f * t2);
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
        if (_sprite.Debug)
        {
            spriteBatch.Draw(_debugTexture, _debugRect, Color.Black);
        }
        
        // Draw the player's social sprite
        if (_chatting)
        {
            _socialSprite.Draw(spriteBatch);
        }

        // Draw the player's sprite
        var sourceRect = new Rectangle(_currentFrame * FrameWidth, direction * FrameHeight, FrameWidth, FrameHeight);
        spriteBatch.Draw(_sprite.Texture, _sprite.Position, sourceRect, Color.White, 0f, _sprite.Origin, _sprite.Scale, SpriteEffects.None, 0);
        
        // Draw the player's name
        spriteBatch.DrawString(_font, _name, _fontShadowPosition, Color.Black);
        spriteBatch.DrawString(_font, _name, _fontPosition, Color.White);
    }
}
