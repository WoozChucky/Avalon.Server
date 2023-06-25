using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using TiledCS;

namespace Avalon.Map
{
    public class ServerMap
    {
        private readonly ICollection<MapLayer> _layers;
        
        public Point MapSize { get; private set; }

        public int Columns { get; private set; }
        public int Rows { get; private set; }
    
        public int TileWidth { get; private set; }
    
        public int TileHeight { get; private set; }
        
        public ServerMap(string mapName, string spriteSheetName)
        {
            _layers = new List<MapLayer>();
            Load(new TiledMap($"Maps/{mapName}.tmx"), spriteSheetName);
        }

        public Tile? this[int layer, int x, int y] => _layers.ElementAt(layer)[x, y];

        public MapLayer? this[int index]
        {
            get
            {
                if (index < 0 || index >= _layers.Count)
                {
                    return null;
                }
                return _layers.ElementAt(index);
            }
        }
        
        public bool IsTileCollidable(int tileX, int tileY, Rectangle boundingBox)
        {
            // Check if the specified tile is collidable
            foreach (var layer in _layers.Where(l => l.IsCollidable))
            {
                var tile = layer[tileX, tileY];

                if (tile is { IsCollidable: true })
                {
                    if (boundingBox.IntersectsWith(tile.Bounds))
                    {
                        Trace.WriteLine($"Intersected rectangle: {{X={tile.Bounds.X}, Y={tile.Bounds.Y}}}. Hero rectangle: {{X={boundingBox.X}, Y={boundingBox.Y}}}");
                        return true;
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
                        return true;
                    }
                }
            }
            return false; // No collision detected
        }
        
        private void Load(TiledMap map, string spriteSheetName)
        {
            // Load the map
            TileWidth = map.TileWidth;
            TileHeight = map.TileHeight;
            Columns = map.Width;
            Rows = map.Height;
        
            MapSize = new Point(Columns * TileWidth, Rows * TileHeight);

            var tilesets = map.GetTiledTilesets("Maps/");
            
            var tileLayers = map.Layers
                .Where(x => x.type == TiledLayerType.TileLayer)
                .OrderBy(x =>
                {
                    return int.Parse(x.properties.FirstOrDefault(p => p.type == TiledPropertyType.Int && p.name == "Order")?.value ?? "0");
                }).ToList();

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

                        layerTiles[x, y] = new Tile(x, y, TileWidth, collidable);
                    }
                }
                _layers.Add(new MapLayer(layerTiles, collidable));
            }
        }
    }
}
