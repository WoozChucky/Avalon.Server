using System;
using Avalon.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Maps;

public class Tile : IDisposable
{
    public readonly Rectangle Bounds;
    private readonly Sprite Sprite;
    private bool isColliding;
    private Texture2D _collidingTexture;
    

    public Tile(Sprite sprite, int x, int y, int width, int height, int xPx, int xPy, bool collidable = false)
    {
        Sprite = sprite;
        IsCollidable = collidable;
        Bounds = new Rectangle(xPx, xPy, width, height);
    }
    
    public int X => Bounds.X;
    public int Y => Bounds.Y;
    public int Width => Bounds.Width;
    public int Height => Bounds.Height;

    public bool IsVisible => Globals.GraphicsDevice.Viewport.Bounds.Intersects(Texture.Bounds);
    public bool IsCollidable { get; protected set; }
    
    public Texture2D Texture => Sprite.Texture;

    public void Draw(SpriteBatch spriteBatch)
    {
        //Texture2D textureToDraw = isColliding ? _collidingTexture : Texture;
        //Sprite.Texture = textureToDraw;
        Sprite.Draw(spriteBatch);
    }
    
    public void MarkAsCollided(bool collided)
    {
        isColliding = collided;

        // Generate a colored version of the tile's texture
        if (isColliding)
        {
            _collidingTexture = CreateColoredTileTexture(Texture, Color.Red);
        }
        else
        {
            _collidingTexture = null;
        }
    }
    
    private static Texture2D CreateColoredTileTexture(Texture2D originalTexture, Color color)
    {
        Color[] data = new Color[originalTexture.Width * originalTexture.Height];
        originalTexture.GetData(data);

        // Create a new texture with the same dimensions as the original
        Texture2D coloredTexture = new Texture2D(originalTexture.GraphicsDevice, originalTexture.Width, originalTexture.Height);

        // Set the color of each pixel in the new texture
        for (int i = 0; i < data.Length; i++)
        {
            // Skip transparent pixels
            if (data[i].A > 0)
            {
                data[i] = color;
            }
        }

        coloredTexture.SetData(data);
        return coloredTexture;
    }

    public void Dispose()
    {
        _collidingTexture?.Dispose();
        Sprite?.Dispose();
    }

    public void ToggleDebug()
    {
        Sprite.Debug = !Sprite.Debug;
    }
}
