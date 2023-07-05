using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Maps;

public class Tile : IDisposable
{
    
    public Rectangle Bounds => _destinationRectangle;
    
    private bool isColliding;
    private Texture2D _collidingTexture;
    
    private readonly Rectangle _sourceRectangle;
    private readonly Rectangle _destinationRectangle;
    private readonly Vector2 _origin;

    private bool _debug;
    
    public int Row { get; set; }
    public int Column { get; set; }
    
    public int Width { get; set; }
    public int Height { get; set; }

    public bool IsVisible
    {
        get
        {
            // You need to adjust this based on how you're handling the camera/view in your game.
            var cameraBounds = new Rectangle(
                (int) Globals.CameraPosition.X, 
                (int) Globals.CameraPosition.Y, 
                Globals.GraphicsDevice.Viewport.Width, 
                Globals.GraphicsDevice.Viewport.Height);

            return cameraBounds.Intersects(_destinationRectangle);
        }
    }

    public bool IsCollidable { get; protected set; }
    
    private Rectangle OutlineRect { get; set; }
    private Texture2D OutlineTexture { get; set; }
    

    public Tile(int x, int y, int width, int height, Rectangle sourceRectangle, Rectangle destinationRectangle, bool collidable = false)
    {
        Column = x;
        Row = y;
        Width = width;
        Height = height;

        _sourceRectangle = sourceRectangle;
        _destinationRectangle = destinationRectangle;
        _origin = new Vector2(_destinationRectangle.Width / 2f, _destinationRectangle.Height / 2f);
        IsCollidable = collidable;

        CreateDebugBorder();
    }
    
    private void CreateDebugBorder()
    {
        var Scale = 1.0f;
        // Calculate the rectangle that encompasses the sprite with the border
        OutlineRect = new Rectangle(
            (int)(_destinationRectangle.X - _origin.X * Scale) - 1,
            (int)(_destinationRectangle.Y - _origin.Y * Scale) - 1,
            (int)(Width * Scale) + 2,
            (int)(Height * Scale) + 2
        );

        // Draw the border using a 1x1 pixel white texture
        OutlineTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        OutlineTexture.SetData(new[] { Color.White });
    }
    
    public void Draw(SpriteBatch spriteBatch, Texture2D atlas)
    {
        if (_debug)
        {
            spriteBatch.Draw(OutlineTexture, OutlineRect, Color.Black);
        }
        spriteBatch.Draw(atlas, _destinationRectangle, _sourceRectangle, Color.White, 0f, _origin, SpriteEffects.None, 0f);
    }
    
    public void MarkAsCollided(bool collided)
    {
        isColliding = collided;

        // Generate a colored version of the tile's texture
        if (isColliding)
        {
            //_collidingTexture = CreateColoredTileTexture(Texture, Color.Red);
        }
        else
        {
            _collidingTexture = null;
        }
    }

    public void Dispose()
    {
        _collidingTexture?.Dispose();
    }

    public void ToggleDebug()
    {
        _debug = !_debug;
    }
}
