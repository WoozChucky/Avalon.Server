using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Maps;

internal class MapEvent
{
    public readonly Rectangle Bounds;
    
    private readonly string _name;
    private readonly int _x;
    private readonly int _y;
    private readonly int _width;
    private readonly int _height;
    private readonly Dictionary<string, string> _properties;
    private readonly Texture2D _texture;
    private readonly Vector2 _origin;
    private readonly Vector2 _position;

    public MapEvent(string name, float x, float y, float width, float height, Dictionary<string,string> properties)
    {
        _name = name;
        _x = (int) x;
        _y = (int) y;
        _width = (int) width;
        _height = (int) height;
        _properties = properties;
        Bounds = new Rectangle((int)x, (int)y, (int)width, (int)height);
        _texture = CreateColoredTileTexture(Color.Black);
        _origin = new Vector2(_width / 2f, _height / 2f);
        _position = new Vector2(_x, _y);
    }
    
    private Texture2D CreateColoredTileTexture(Color color)
    {
        Color[] data = new Color[_width * _height];

        // Create a new texture with the same dimensions as the original
        var coloredTexture = new Texture2D(Globals.GraphicsDevice, _width, _height);

        // Set the color of each pixel in the new texture
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = color;
        }

        coloredTexture.SetData(data);
        return coloredTexture;
    }
    
    public void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(_texture, _position, null, Color.White, 0f, _origin, 1f, SpriteEffects.None, 0f);
    }
    
    public void Dispose()
    {
        _texture?.Dispose();
    }
}
