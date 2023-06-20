using System;
using System.Collections.Generic;

namespace Avalon.Client.Maps;

public class Layer : IDisposable
{
    private readonly Tile[,] _tiles;
    
    public Layer(Tile[,] tiles, bool collidable = false)
    {
        _tiles = tiles;
        IsCollidable = collidable;
    }

    public bool IsCollidable { get; private set; }
    
    public Tile this[int x, int y]
    {
        get
        {
            if (x < 0 || x >= _tiles.GetLength(0) || y < 0 || y >= _tiles.GetLength(1))
            {
                return null;
            }
            return _tiles[x, y];
        }
    }

    public void Dispose()
    {
        if (_tiles != null)
        {
            foreach (var tile in _tiles)
            {
                tile?.Dispose();
            }
        }
    }
}
