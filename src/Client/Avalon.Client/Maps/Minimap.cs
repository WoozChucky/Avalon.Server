using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Maps;

public class Minimap
{
    private RenderTarget2D _renderTarget;
    private Texture2D _minimapTexture;
    
    private Texture2D _borderTexture;
    private Vector2 _borderPosition;
    private Rectangle _borderRectangle;
    
    private Map _map;
    
    private const int MinimapWidth = 200;
    private const int MinimapHeight = 200;

    private float _minimapScaleX;
    private float _minimapScaleY;
    
    private readonly SpriteBatch _spriteBatch;

    public Minimap()
    {
        _spriteBatch = new SpriteBatch(Globals.GraphicsDevice);
        _borderTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        _borderTexture.SetData(new[] { Color.Brown });
    }

    public void Load(Map map)
    {
        _map = map;
        
        _renderTarget = new RenderTarget2D(Globals.GraphicsDevice, MinimapWidth, MinimapHeight);
    }

    public void Generate()
    {
        Globals.GraphicsDevice.SetRenderTarget(_renderTarget);
        Globals.GraphicsDevice.Clear(Color.Transparent);
        
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
        
        // Calculate the minimap scales to fit the entire map within the minimap dimensions
        _minimapScaleX = (float)MinimapWidth / _map.MapSize.X * _map.TileWidth;
        _minimapScaleY = (float)MinimapHeight / _map.MapSize.Y * _map.TileHeight;
        
        foreach (var layer in _map.Layers)
        {
            for (int y = 0; y <= _map.Rows; y++)
            {
                for (int x = 0; x <= _map.Columns; x++)
                {
                    if (x >= 0 && x < _map.Columns && y >= 0 && y < _map.Rows)
                    {
                        var tile = layer[x, y];

                        if (tile is null)
                        {
                            continue;
                        }

                        tile.DrawOnMinimap(_spriteBatch, _map.MapAtlas, _minimapScaleX, _minimapScaleY, x, y);
                    }
                }
            }
        }
        
        _spriteBatch.End();
        
        Globals.GraphicsDevice.SetRenderTarget(null);
        
        // Assign the minimap render target to the minimap texture for drawing
        _minimapTexture = (Texture2D)_renderTarget;
        
    }
    
    public void Update(float deltaTime)
    {
        var borderPosition = Globals.CameraPosition + new Vector2(730, 16);
        var borderWidth = _minimapTexture.Width + 4;
        var borderHeight = _minimapTexture.Height + 4;
        
        _borderPosition = Globals.CameraPosition + new Vector2(730, 16);
        _borderRectangle = new Rectangle((int)borderPosition.X - 2, (int)borderPosition.Y - 2, borderWidth, borderHeight);
    }
    
    public void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(_minimapTexture, Globals.CameraPosition + new Vector2(730, 16), Color.White);
        
        // Draw the border rectangle with a black color
        DrawRectangle(spriteBatch, _borderRectangle, Color.Brown, 2);
        
        // Draw creatures position
        // Draw other players position
        // Draw player position
        
    }
    
    private void DrawRectangle(SpriteBatch spriteBatch, Rectangle rectangle, Color color, int thickness)
    {
        // Draw the top line
        spriteBatch.Draw(_borderTexture, new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, thickness), color);
        // Draw the bottom line
        spriteBatch.Draw(_borderTexture, new Rectangle(rectangle.X, rectangle.Y + rectangle.Height - thickness, rectangle.Width, thickness), color);
        // Draw the left line
        spriteBatch.Draw(_borderTexture, new Rectangle(rectangle.X, rectangle.Y, thickness, rectangle.Height), color);
        // Draw the right line
        spriteBatch.Draw(_borderTexture, new Rectangle(rectangle.X + rectangle.Width - thickness, rectangle.Y, thickness, rectangle.Height), color);
    }
    
    public void Dispose()
    {
        _renderTarget?.Dispose();
        _minimapTexture?.Dispose();
        _spriteBatch?.Dispose();
    }
}
