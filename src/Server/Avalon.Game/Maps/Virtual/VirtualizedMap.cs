using System.Diagnostics;
using System.Drawing;
using Avalon.Common.Extensions;
using Avalon.VirtualMap;
using Microsoft.Extensions.Logging;

namespace Avalon.Game.Maps.Virtual;

public class VirtualizedMap
{
    private readonly ILogger<VirtualizedMap> _logger;
    private readonly string _directory;
    private readonly ICollection<TileLayer> _layers;
    private readonly ICollection<MapCreaturePool> _creaturePools;
    private readonly ICollection<MapCreature> _creatures;
    private readonly ICollection<MapObject> _objects;
    private readonly ICollection<MapEvent> _events;

    public Point Size { get; private set; }

    public int Id { get; private set; }

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    public int TileWidth { get; private set; }

    public int TileHeight { get; private set; }
    
    public byte[] TmxData { get; private set; }
    public byte[][] TsxData { get; private set; }

    
    public VirtualizedMap(ILoggerFactory loggerFactory, int id, string name, string directory)
    {
        Id = id;
        _directory = directory;
        _logger = loggerFactory.CreateLogger<VirtualizedMap>();
        _layers = new List<TileLayer>();
        _creatures = new List<MapCreature>();
        _objects = new List<MapObject>();
        _events = new List<MapEvent>();
        _creaturePools = new List<MapCreaturePool>();
        TmxData = File.ReadAllBytes($"{directory}{name}");
        TsxData = Array.Empty<byte[]>();
        Load(new TiledMap(TmxData.ToMemoryStream()));
    }

    public IMapElement? this[MapElementType type, int layer, int x, int y]
    {
        get
        {
            return type switch
            {
                MapElementType.Creature => _creatures.FirstOrDefault(c =>
                    (int)c.Position.X == x && (int)c.Position.Y == y),
                MapElementType.Object => _objects.FirstOrDefault(o =>
                    (int)o.Position.X == x && (int)o.Position.Y == y),
                MapElementType.Event => _events.FirstOrDefault(
                    e => (int)e.Position.X == x && (int)e.Position.Y == y),
                MapElementType.Tile => _layers.ElementAt(layer)[x, y],
                MapElementType.CreaturePool => _creaturePools.FirstOrDefault(p =>
                    (int)p.Position.X == x && (int)p.Position.Y == y),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }
    }

    public MapTile? this[int layer, int x, int y] => _layers.ElementAt(layer)[x, y];

    public TileLayer? this[int index]
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
    
    public IEnumerable<MapCreature> Creatures => _creatures;
    
    public ICollection<MapEvent> Events => _events;

    private bool IsTileCollidable(int tileX, int tileY, Rectangle boundingBox)
    {
        // Check if the specified tile is collidable
        foreach (var layer in _layers)
        {
            if (!layer.IsCollidable) continue;
            
            var tile = layer[tileX, tileY];

            if (tile is not { IsCollidable: true }) continue;
            
            if (!boundingBox.IntersectsWith(tile.Bounds)) continue;
            
            _logger.LogTrace("Intersected rectangle: {{X={BoundsX}, Y={BoundsY}}}. Hero rectangle: {{X={BoundingBoxX}, Y={BoundingBoxY}}}", 
                tile.Bounds.X, tile.Bounds.Y, boundingBox.X, boundingBox.Y);
            return true;
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
                    _logger.LogTrace("Collision detected at: {X}, {Y}", x, y);
                    return true;
                }
            }
        }
        return false; // No collision detected
    }
    
    private void Load(TiledMap map)
    {
        // Load the map
        TileWidth = map.TileWidth;
        TileHeight = map.TileHeight;
        Columns = map.Width;
        Rows = map.Height;

        Size = new Point(Columns * TileWidth, Rows * TileHeight);
        
        TsxData = new byte[map.Tilesets.Length][];
        for (var i = 0; i < map.Tilesets.Length; i++)
        {
            var tsx = File.ReadAllBytes($"{_directory}{map.Tilesets[i].source}");
            TsxData[i] = tsx;
        }

        // Load object layers
        {
            var objectLayers = map.Layers
                .Where(x => x.type == TiledLayerType.ObjectLayer)
                .ToList();
            
            var objectLayer = objectLayers.FirstOrDefault(x => x.name == "Objects");
            var creatureLayer = objectLayers.FirstOrDefault(x => x.name == "Creatures");
            var creaturePoolsLayer = objectLayers.FirstOrDefault(x => x.name == "CreaturePools");
            var eventLayer = objectLayers.FirstOrDefault(x => x.name == "Events");
            
            if (objectLayer != null)
            {
                //TODO: Load objects
            }

            if (creatureLayer != null)
            {
                foreach (var tileObject in creatureLayer.objects)
                {
                    var id = int.Parse(tileObject.properties.FirstOrDefault(p => p.type == TiledPropertyType.Int && p.name == "TemplateId")?.value ?? "0");
                    _creatures.Add(new MapCreature(id, (int) tileObject.x, (int) tileObject.y, (int) tileObject.width));
                }
            }

            if (creaturePoolsLayer != null)
            {
                //TODO: Load creature pools
            }

            if (eventLayer != null)
            {
                foreach (var mapEvent in eventLayer.objects)
                {
                    var properties = new Dictionary<string, string>();
                    
                    foreach (var objectProperties in mapEvent.properties)
                    {
                        properties.Add(objectProperties.name, objectProperties.value);
                    }

                    _events.Add(new MapEvent(mapEvent.id, mapEvent.name, mapEvent.@class, (int) mapEvent.x, (int) mapEvent.y, (int) mapEvent.width, (int) mapEvent.height));
                }
            }
        }

        // Load tile layers
        {
            var tileLayers = map.Layers
                .Where(x => x.type == TiledLayerType.TileLayer)
                .OrderBy(x =>
                {
                    return int.Parse(x.properties.FirstOrDefault(p => p.type == TiledPropertyType.Int && p.name == "Order")?.value ?? "0");
                }).ToList();

            foreach (var layer in tileLayers)
            {
                var collidable = layer.properties.FirstOrDefault(p => p.type == TiledPropertyType.Bool && p.name == "Collidable")?.value == "true";
                var layerTiles = new MapTile[Columns, Rows];
            
                for (int y = 0; y < layer.height; y++)
                {
                    for (int x = 0; x < layer.width; x++)
                    {
                        var index = (y * layer.width) + x; // Assuming the default render order used is right-down
                        var gid = layer.data[index]; // The tileset tile index

                        // Gid 0 is used to tell there is no tile set
                        if (gid == 0) continue;
                        
                        var tileX = x * TileWidth;
                        var tileY = y * TileHeight;

                        layerTiles[x, y] = new MapTile(x, y, tileX, tileY, TileWidth, collidable);
                    }
                }
                _layers.Add(new TileLayer(layerTiles, collidable));
            }
        }
    }
}
