using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TiledCS;

namespace Avalon.Client.Maps;

public class Map : IDisposable
{
    private List<Layer> _layers;
    private List<Layer> _collidableLayers;
    
    private List<MapEvent> _events;
    
    private Texture2D _mapAtlas;
    
    public Point MapSize { get; private set; }

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    
    public int TileWidth { get; private set; }
    
    public int TileHeight { get; private set; }
    
    public Map(string mapName, string directory, string spriteSheetName)
    {
        Load(new TiledMap($"{directory}{mapName}"), spriteSheetName);
    }
    
    private void Load(TiledMap map, string spriteSheetName)
    {
        // Load the map
        TileWidth = map.TileWidth;
        TileHeight = map.TileHeight;
        Columns = map.Width;
        Rows = map.Height;
    
        MapSize = new Point(Columns * TileWidth, Rows * TileHeight);
        
        _layers = new List<Layer>();
        _events = new List<MapEvent>();
        
        var tilesets = map.GetTiledTilesets("Maps/");
        
        var tileLayers = map.Layers
            .Where(x => x.type == TiledLayerType.TileLayer)
            .OrderBy(x =>
            {
                return int.Parse(x.properties.FirstOrDefault(p => p.type == TiledPropertyType.Int && p.name == "Order")?.value ?? "0");
            }).ToList();
        
        var eventLayers = map.Layers
            .Where(x => x.type == TiledLayerType.ObjectLayer)
            .OrderBy(x =>
            {
                return int.Parse(x.properties.FirstOrDefault(p => p.type == TiledPropertyType.Int && p.name == "Order")?.value ?? "0");
            }).ToList();

        foreach (var eventLayer in eventLayers)
        {
            foreach (var mapObject in eventLayer.objects)
            {
                var properties = new Dictionary<string, string>();
                
                foreach (var property in mapObject.properties)
                {
                    properties.Add(property.name, property.value);
                }
                _events.Add(new MapEvent(mapObject.name, mapObject.x, mapObject.y, mapObject.width, mapObject.height, properties));
            }
        }

        _mapAtlas = Globals.Content.Load<Texture2D>($"Images/{spriteSheetName}");

        foreach (var layer in tileLayers)
        {
            var collidable = layer.properties.FirstOrDefault(p => p.type == TiledPropertyType.Bool && p.name == "Collidable")?.value == "true";
            var layerTiles = new Tile[Columns, Rows];
            
            for (int y = 0; y < layer.height; y++)
            {
                for (int x = 0; x < layer.width; x++)
                {
                    var index = (y * layer.width) + x; // Assuming the default render order used is right-down
                    var gid = layer.data[index]; // The tileset tile index
                    var tileX = x * TileWidth;
                    var tileY = y * TileHeight;

                    // Gid 0 is used to tell there is no tile set
                    if (gid == 0) continue;

                    // This is a connection object Tiled uses for linking the correct tileset to the gid value using the first gid property
                    var mapTileset = map.GetTiledMapTileset(gid);
                    
                    var tileset = tilesets[mapTileset.firstgid];

                    var rect = map.GetSourceRect(mapTileset, tileset, gid);
                    
                    var source = new Rectangle(rect.x, rect.y, rect.width, rect.height);
                    var destination = new Rectangle(tileX, tileY, TileWidth, TileHeight);
                    
                    layerTiles[x, y] = new Tile(x, y, TileWidth, TileHeight, source, destination, collidable);
                }
            }
            
            _layers.Add(new Layer(layerTiles, collidable));
        }
        _collidableLayers = _layers.Where(l => l.IsCollidable).ToList();
    }

    #region Collision Detection

    public bool IsTileCollidable(int tileX, int tileY, Rectangle boundingBox)
    {
        // Check if the specified tile is collidable
        foreach (var layer in _collidableLayers)
        {
            var tile = layer[tileX, tileY];

            if (tile != null)
            {
                if (tile is { IsCollidable: true })
                {
                    if (boundingBox.Intersects(tile.Bounds))
                    {
                        tile.MarkAsCollided(true);
                        return true;
                    }
                }
                else
                {
                    tile.MarkAsCollided(false);
                }
            }
        }
        
        return false;
    }
    
    public bool IsObjectColliding(Rectangle boundingBox)
    {
        // Convert the bounding box into tile coordinates
        var leftTile = (int)Math.Floor((float)boundingBox.Left / TileWidth);
        var rightTile = (int)Math.Ceiling((float)boundingBox.Right / TileWidth);
        var topTile = (int)Math.Floor((float)boundingBox.Top / TileHeight);
        var bottomTile = (int)Math.Ceiling((float)boundingBox.Bottom / TileHeight);

        // Iterate over the tiles within the bounding box and check for collision
        for (var y = topTile; y <= bottomTile; y++)
        {
            for (var x = leftTile; x <= rightTile; x++)
            {
                if (IsTileCollidable(x, y, boundingBox))
                {
                    Trace.WriteLine("Collistion detected at: " + x + ", " + y + "");
                    // Collision detected with collidable tile
                    return true;
                }
            }
        }
        return false; // No collision detected
    }

    #endregion
    
    public void ToggleDebug()
    {
        foreach (var layer in _layers.Where(l => l.IsCollidable))
        {
            layer.ToggleDebug();
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        // Instead of Drawing all the tiles to the screen, we check if the tile is visible first
        
        // Get the start and end indices of the visible tiles
        var startX = Math.Max(Globals.GraphicsDevice.Viewport.X / TileWidth + (int)(Globals.CameraPosition.X / TileWidth), 0);
        var startY = Math.Max(Globals.GraphicsDevice.Viewport.Y / TileHeight + (int)(Globals.CameraPosition.Y / TileHeight), 0);
        var endX = Math.Min((Globals.GraphicsDevice.Viewport.X + Globals.GraphicsDevice.Viewport.Width) / TileWidth + (int)(Globals.CameraPosition.X / TileWidth) + 1, Columns);
        var endY = Math.Min((Globals.GraphicsDevice.Viewport.Y + Globals.GraphicsDevice.Viewport.Height) / TileHeight + (int)(Globals.CameraPosition.Y / TileHeight) + 1, Rows);

        // Ensure the tile indices are within valid range
        startX = MathHelper.Clamp(startX, 0, Columns - 1);
        startY = MathHelper.Clamp(startY, 0, Rows - 1);
        endX = MathHelper.Clamp(endX, 0, Columns - 1);
        endY = MathHelper.Clamp(endY, 0, Rows - 1);

        // Draw the visible tiles including an additional tile offscreen to offset the camera movement flickering
        foreach (var layer in _layers)
        {
            for (int y = startY - 1; y <= endY + 1; y++)
            {
                for (int x = startX - 1; x <= endX + 1; x++)
                {
                    if (x >= 0 && x < Columns && y >= 0 && y < Rows)
                    {
                        var tile = layer[x, y];

                        if (tile is not { IsVisible: true })
                        {
                            continue;
                        }
                        tile.Draw(spriteBatch, _mapAtlas);
                    }
                }
            }
        }

        // Draw the map events
        foreach (var mapEvent in _events)
        {
            //mapEvent.Draw(spriteBatch);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    layer.Dispose();
                }
                _layers.Clear();
                _layers = null;
            }
            if (_events != null)
            {
                foreach (var mapEvent in _events)
                {
                    mapEvent.Dispose();
                }
                _events.Clear();
                _events = null;
            }
            _mapAtlas?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
